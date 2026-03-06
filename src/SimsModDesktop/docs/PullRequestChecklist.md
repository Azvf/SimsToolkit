# Pull Request Checklist

提交结构性改动前，请逐项确认。

---

## 1. 分层与放置

- 新增类型是否放在了最自然的项目中，而不是“哪个项目先能引用就放哪”？
- 仅属于 `Program` / `App` / `Window` / `UserControl` 生命周期的代码，是否放在 `src/SimsModDesktop`？
- 仅属于 ViewModel / UI 控制器流程的代码，是否放在 `src/SimsModDesktop.Presentation`？
- 纯业务规划、校验、编排是否仍位于 `src/SimsModDesktop.Application`？
- 具体文件系统、哈希、配置、平台实现是否仍位于 `src/SimsModDesktop.Infrastructure`？

## 2. 依赖方向

- 是否引入了 `Application -> Presentation` 反向依赖？
- 是否引入了 `Application -> Infrastructure` 直接依赖？
- 是否引入了 `Presentation -> Infrastructure` 直接依赖？
- 是否引入了 `Feature Engine -> Presentation` 反向依赖？
- 是否因为图省事把宿主级实现塞进了 `Presentation` 并提升成 `public`？
- 如果 `Presentation` 或 `Application` 需要某个基础设施能力，是否改为依赖 `Application` 契约并通过 Host DI 组合，而不是直接加项目引用？
- 如果发生跨程序集访问，是否确认这是共享能力而不是文件放错位置？

## 3. 目录与命名空间

- 文件路径与命名空间前缀是否一致？
- `src/SimsModDesktop/Diagnostics/*` 是否都使用 `SimsModDesktop.Diagnostics`？
- `src/SimsModDesktop.Presentation/Diagnostics/*` 是否都使用 `SimsModDesktop.Presentation.Diagnostics`？
- 是否存在“文件在 A 项目，命名空间看起来像 B 项目”的伪装情况？

## 4. 可见性与 API 面

- 新增类型是否默认使用最小可见性（优先 `internal`）？
- 是否只因另一个项目要用，就把实现类提升为 `public`？
- 若新增公共类型，是否确实是稳定 API，而不是临时实现细节？

## 5. View / ViewModel / Controller

- View 代码隐藏是否只做事件桥接、焦点处理、可视树钩子？
- 是否把复杂流程继续堆回 `MainWindowViewModel`，而不是放入现有 Controller？
- Controller 是否仍只负责 UI 流程协调，而不是承载纯业务算法？

## 6. 日志与诊断

- 宿主生命周期诊断（启动、首屏可见）是否放在 Desktop Host？
- Presentation 流程耗时打点是否放在 `Presentation/Diagnostics`？
- 结构化日志和 UI 文本日志的职责是否清晰，没有混成一个不可维护的层？
- 高频过程是否避免了逐项刷屏式日志？

## 7. DI 与测试

- 构造函数签名变更后，是否同步修复了所有测试中的手工 `new`？
- 新增 `ILogger<T>` 注入后，测试是否使用 `NullLogger<T>.Instance` 或等价替身？
- 是否补了行为测试或架构守卫测试来覆盖这次新增规则？

## 8. 文档

- 这次改动是否改变了“后续开发者该怎么放代码”的默认规则？
- 如果改变了，是否同步更新了 `EngineeringConventions.md` 或相关 docs？

---

## 9. 最低通过标准

在提交前，至少满足：

- 项目可构建
- 相关测试通过
- 没有新增反向依赖
- 没有新增放错层且靠 `public` 硬撑的类型

如果上述任一项不满足，不应合并。
