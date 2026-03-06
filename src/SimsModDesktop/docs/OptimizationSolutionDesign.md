# 优先事项落地设计方案（2026-03）

## 1. 目标与约束

### 1.1 目标

围绕以下优先项做结构化收敛，并做到“可验证、可门禁、可持续”：

1. SQLite 生命周期优化
2. 配置与文档一致性治理
3. 可空性告警清零并门禁化
4. 用例层收敛（NoOp 去库存）
5. 热点大文件拆分，降低缺陷率和迭代成本

### 1.2 约束

* 不回退当前页面触发型 warmup 机制（不恢复启动期隐式预热）。
* 不引入 `Application -> Presentation` 反向依赖。
* 每一阶段必须有 CI 可执行门禁，避免“治理一次、回归长期”。

---

## 2. 当前基线（已确认）

### 2.1 SQLite 维护能力不足

* `AppCacheDatabase` 与两个 `SqliteCacheDatabase` 仅设置 `journal_mode=WAL`、`synchronous=NORMAL`。
* `IAppCacheMaintenanceService` / `AppCacheMaintenanceService` 仅提供清理文件夹/文件（`ClearAsync` / `ClearAllAsync`），无 `checkpoint / optimize / vacuum` 生命周期维护。

### 2.2 配置与文档漂移

* `DebugConfigTable.md` 仍保留 `startup.tray_cache_warmup.*`。
* `CacheWarmupSequence.md` 明确“当前不存在启动期 Tray warmup”。
* `ShellSettingsController` 的 `DebugToggleDefinitions` 当前为空，与文档说明不一致。
* README 平台标识为 `Windows | macOS`，CI workflow 当前仅 `windows-latest`。

### 2.3 告警尚未清零且无强制门禁

`dotnet build` 仍存在告警（当前 11 条）：

* `CS8601`（生产代码）2 条
* `CS0067`（测试桩事件）多条

### 2.4 NoOp UseCase 为库存债务

* `NoOpUseCases.cs` 中存在 14 个 `NoOp*UseCase`。
* 这些接口目前主要停留在 DI 注册与服务注册测试，业务主路径几乎不消费该抽象。

### 2.5 热点大文件集中

当前高风险文件（行数）：

* `PackageIndexCache.cs` 2540
* `PreviewQueryService.cs` 2118
* `TrayDependencyExportService.cs` 1736
* `SqliteModItemIndexStore.cs` 1393

---

## 3. 总体执行策略

采用“双轨并行、门禁前置”：

* 质量轨：优先建立告警/文档/大文件的自动门禁，先卡住回归。
* 架构轨：分批重构 SQLite 生命周期、UseCase 收敛和大文件拆分。

分 4 个迭代批次（每批均可独立合并）：

1. **Batch A（P0）**：可空性清零 + 告警门禁 + 文档漂移最小修复
2. **Batch B（P1）**：SQLite 生命周期维护能力上线
3. **Batch C（P1）**：UseCase 收敛（NoOp 去库存）
4. **Batch D（P2）**：热点大文件拆分 + 行数门禁

---

## 4. 方案细节

## 4.1 SQLite 生命周期优化

### 4.1.1 设计目标

* 缓解 WAL 膨胀与长期碎片导致的 I/O 抖动。
* 维护操作可手动触发、可自动低频触发、可观测。
* 维护失败不影响主流程可用性（降级而非崩溃）。

### 4.1.2 合约扩展

在 `IAppCacheMaintenanceService` 增加维护接口（示意）：

```csharp
Task<AppCacheMaintenanceResult> MaintainAsync(
    AppCacheMaintenanceMode mode,
    CancellationToken cancellationToken = default);
```

新增枚举：

* `Light`: `wal_checkpoint(TRUNCATE)` + `PRAGMA optimize`
* `Standard`: `Light + ANALYZE`
* `Deep`: `Standard + VACUUM`（仅手动）

### 4.1.3 执行模型

* 目标库：
  * `app-cache.db`
  * `TrayDependencyPackageIndex/cache.db`（可配置是否包含）
* 每库串行锁（`ConcurrentDictionary<string, SemaphoreSlim>`）避免并发维护冲突。
* 维护结果带指标：执行耗时、维护前后 `db/wal/shm` 大小、是否成功、错误摘要。

### 4.1.4 触发策略

* 手动入口：Shell 系统操作中新增“优化缓存数据库”命令。
* 自动入口：在高写入路径后以节流策略触发 `Light`（例如 24h 一次），不阻塞 UI 主路径。
* `Deep` 仅允许用户显式触发。

### 4.1.5 验收

* 集成测试验证 `MaintainAsync` 在真实 sqlite 文件上可执行并返回指标。
* 日志新增：`cache.sqlite.maintain.start/done/fail`。
* 大样本数据上 wal 文件峰值可回落，warmup 抖动下降。

---

## 4.2 配置与文档一致性治理

### 4.2.1 单一事实源

* 以代码侧 `DebugToggleDefinitions`（或其替代目录）为配置键事实源。
* 文档从事实源生成或校验，避免双向手工维护。

### 4.2.2 治理机制

新增 `scripts/docs/verify-config-docs.ps1`，校验：

1. 文档 `DebugConfigTable.md` 的 key 列表与代码定义一致。
2. README 平台声明与 `.github/workflows/dotnet-ci.yml` matrix 一致。
3. Warmup 行为描述与 `CacheWarmupSequence.md` 保持一致术语（禁止再出现“startup tray warmup enabled”旧描述）。

### 4.2.3 CI 门禁

新增 `docs-governance` job：

* 执行脚本校验
* 失败即阻断 PR

### 4.2.4 验收

* 误改文档或 workflow 时可被 CI 立即拦截。
* `DebugConfigTable.md` 清理掉失效的 `startup.tray_cache_warmup.*` 条目。

---

## 4.3 可空性告警清零并门禁化

### 4.3.1 清零策略

先修现存告警，再上门禁：

1. 修复生产代码 `CS8601`。
2. 清理测试桩 `CS0067`（改为显式 `add/remove` 空实现事件或调整桩设计）。

### 4.3.2 门禁策略

CI build 增加：

```bash
dotnet build SimsDesktopTools.sln \
  --configuration Release \
  -warnaserror:CS8600,CS8601,CS8602,CS8603,CS8604,CS8618
```

说明：先对 NRT 高风险告警门禁，再评估是否扩展为全告警门禁。

### 4.3.3 验收

* 主干 NRT 告警保持 0。
* 新增可空风险 PR 无法合并。

---

## 4.4 用例层收敛（NoOp 去库存）

### 4.4.1 现状判断

`Application.UseCases` 当前为“空请求/空结果 + NoOp 实现”模式，且业务主路径基本不依赖该层；继续保留会形成“看似可用、实际空实现”的风险。

### 4.4.2 收敛原则

* 以现有真实应用服务接口作为主边界（`IExecutionCoordinator`、`IPreviewQueryService`、`IModItemCatalogService`、`IGameLaunchService` 等）。
* 下线无业务承载价值的 NoOp UseCase 套件。

### 4.4.3 迁移步骤

1. 移除 `NoOpUseCases.cs` 与对应 DI 注册。
2. 调整服务注册测试，改为断言真实服务边界。
3. 若必须保留 UseCase 命名层，则引入薄适配器（调用真实服务并返回有语义的结果），禁止再出现 NoOp success。
4. 增加架构守卫：禁止新增 `NoOp*UseCase`。

### 4.4.4 验收

* NoOp UseCase 数量降至 0。
* 服务注册测试覆盖真实能力而非占位实现。

---

## 4.5 热点大文件拆分

### 4.5.1 拆分优先级

1. `PackageIndexCache.cs`
2. `PreviewQueryService.cs`
3. `TrayDependencyExportService.cs`
4. `SqliteModItemIndexStore.cs`

### 4.5.2 拆分边界（示意）

`PackageIndexCache.cs`：

* `PackageIndexSchemaManager`（schema/version/migration）
* `PackageIndexBuildPlanner`（增量计划）
* `PackageIndexBatchWriter`（批量持久化）
* `SqliteTrayDependencyLookup*`（查询会话）
* `LruCache` 独立工具文件

`PreviewQueryService.cs`：

* Root snapshot 构建
* 过滤/分页投影
* 元数据索引访问
* 缩略图与缓存协同

`TrayDependencyExportService.cs`：

* `TrayBundleLoader`
* `DirectMatchEngine`
* `DependencyExpandEngine`
* `ModFileExporter`
* `StructuredDependencyReaders` 族

`SqliteModItemIndexStore.cs`：

* Schema 与 migration
* 写入仓储（批写）
* 查询仓储（分页/计数/纹理）
* QueryPlan 与 row model 文件化

### 4.5.3 大文件门禁

新增脚本 `scripts/quality/verify-large-files.ps1`：

* 生产代码阈值建议：`> 800` 行报警，`> 1000` 行阻断（可分阶段启用）。
* 仅对 `src/**` 生产项目生效，测试项目单独阈值。

### 4.5.4 验收

* 四个热点文件完成首轮拆分。
* 新增超阈值大文件可被 CI 阻断。

---

## 5. PR 切分建议（可直接执行）

1. `PR-1`：修复现有可空性/测试告警 + NRT 门禁接入 CI
2. `PR-2`：DebugConfig/Warmup/README 与 CI 一致性修复 + docs-governance job
3. `PR-3`：`IAppCacheMaintenanceService.MaintainAsync` + SQLite 维护实现 + 测试
4. `PR-4`：移除 NoOp UseCase + 更新 DI 与测试 + 守卫测试
5. `PR-5`：`PackageIndexCache` 拆分
6. `PR-6`：`PreviewQueryService` 拆分
7. `PR-7`：`TrayDependencyExportService` + `SqliteModItemIndexStore` 拆分
8. `PR-8`：大文件门禁脚本与 CI

---

## 6. 风险与回滚

### 6.1 SQLite 维护引发锁竞争

* 规避：串行锁 + 短事务 + 失败降级。
* 回滚：保留 `ClearAsync/ClearAllAsync` 作为兜底路径。

### 6.2 大文件拆分造成行为回归

* 规避：每次拆分先抽纯函数/只读查询，再抽写入路径。
* 回滚：拆分按 PR 粒度进行，可逐个回退。

### 6.3 NoOp 下线影响隐含依赖

* 规避：先全局检索实际消费点，必要时临时适配器过渡。
* 回滚：保留短期 feature flag（仅过渡期）。

---

## 7. 完成定义（Definition of Done）

以下全部满足即视为本轮优先项完成：

1. NRT 告警在 CI 为 0 且门禁生效
2. 文档与配置键一致性校验纳入 CI
3. SQLite 维护能力可调用、有指标、有测试
4. NoOp UseCase 去库存完成
5. 热点文件拆分完成并启用大文件门禁
