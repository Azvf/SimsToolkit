# 项目工程规范（当前基线）

本文档用于约束当前 `SimsToolkit` 仓库的工程边界、分层职责和常见实现规则，避免在后续演进中再次出现“功能可用但结构越层”或“放置位置不一致”的问题。

适用范围：

* `src/SimsModDesktop`
* `src/SimsModDesktop.Application`
* `src/SimsModDesktop.Presentation`
* `src/SimsModDesktop.Infrastructure`
* `src/SimsModDesktop.PackageCore`
* `src/SimsModDesktop.SaveData`
* `src/SimsModDesktop.TrayDependencyEngine`
* `src/SimsModDesktop.Tests`

---

## 1. 目标

本仓库当前以“清晰分层 + 稳定依赖方向 + 可测试演进”为主目标。

核心原则：

* 代码首先要服从分层边界，其次才是实现便利。
* 同一类职责应放在同一层，避免“临时借位”演变成长期结构债务。
* 新增代码默认要与现有 DI、控制器、测试构造模式兼容。
* 功能扩展可以先做最小实现，但目录、命名空间和依赖方向不能先乱。

---

## 2. 解决方案分层

### 2.1 Desktop Host

项目：`src/SimsModDesktop`

职责：

* Avalonia 应用入口
* App 生命周期
* 桌面壳视图与窗口
* 组合根（composition root）
* 仅属于桌面壳生命周期的代码

可放内容：

* `Program.cs`
* `App.axaml.cs`
* Avalonia `Window` / `UserControl` 代码隐藏
* `Composition/ServiceCollectionExtensions.cs`
* 只被桌面壳入口、窗口打开、首屏渲染等生命周期使用的诊断代码

不应放内容：

* 业务规划逻辑
* 跨模块业务协调器
* 面板状态模型
* 可复用的应用层 contracts

### 2.2 Presentation

项目：`src/SimsModDesktop.Presentation`

职责：

* ViewModel
* UI 控制器
* 导航状态
* 面板状态
* UI 交互 contracts（对话框、导航、Launcher）
* 只服务于 UI 流程的本层辅助类

可放内容：

* `MainWindowExecutionController`
* `MainWindowLifecycleController`
* `MainWindowTrayPreviewController`
* `MainShellViewModel`
* `RelayCommand` / `AsyncRelayCommand`
* 仅供 Presentation 控制器使用的 timing/logging helper

不应放内容：

* 桌面壳生命周期专用逻辑
* 具体文件系统/SQLite/OS 实现
* 需要被 Desktop Host 跨程序集消费、但语义又不属于 UI 控制器的“宿主级”组件

### 2.3 Application

项目：`src/SimsModDesktop.Application`

职责：

* typed request / plan / result
* planner、validator、coordinator
* 业务用例编排
* 纯业务 contracts

可放内容：

* `ToolkitActionPlanner`
* `ExecutionCoordinator`
* `IExecutionCoordinator`
* `ITrayPreviewCoordinator`
* 各类 `Input` / `Plan` / `Result`

不应放内容：

* Avalonia 类型
* 具体 UI 文本/状态栏操作
* 具体文件 IO / 平台 API 实现

### 2.4 Infrastructure

项目：`src/SimsModDesktop.Infrastructure`

职责：

* 文件系统
* 哈希
* 配置
* 路径发现
* SQLite-backed store
* 具体平台适配

可放内容：

* `CrossPlatformFileOperationService`
* `CrossPlatformHashComputationService`
* `CrossPlatformConfigurationProvider`

不应放内容：

* ViewModel
* UI 控制器
* Avalonia 对话框窗口逻辑

### 2.5 Feature Engines / Core Libraries

项目：

* `src/SimsModDesktop.PackageCore`
* `src/SimsModDesktop.SaveData`
* `src/SimsModDesktop.TrayDependencyEngine`

职责：

* 高内聚、可复用的功能域能力
* 不依赖 Avalonia
* 不承载 Presentation 细节

---

## 3. 依赖方向

当前允许的主方向：

* `Desktop Host -> Presentation`
* `Desktop Host -> Application`
* `Desktop Host -> Infrastructure`
* `Presentation -> Application`
* `Presentation -> Feature Engines`
* `Application -> Infrastructure.Abstractions`（通过接口或 contracts 间接依赖）
* `Infrastructure -> Application.Contracts`

当前禁止的方向：

* `Application -> Presentation`
* `Infrastructure -> Presentation`（特殊共享 contract 命名空间除外，但应尽量避免扩散）
* `Feature Engines -> Presentation`
* `Presentation` 中放置必须由 `Desktop Host` 直接消费的“宿主级专用代码”

判断规则：

* 如果一个类型的语义只在 `Program/App/Window/Control` 生命周期成立，它应优先属于 `Desktop Host`。
* 如果一个类型的语义只在 ViewModel/Controller 内部成立，它应优先属于 `Presentation`。
* 如果一个类型未来可能被多个层直接引用，不要先“借放”在某一层，先判断是否应该抽成共享 contracts 或独立项目。

---

## 4. 目录与命名空间规则

### 4.1 目录必须表达职责

新增文件的目录应优先表达“层 + 职责”。

示例：

* `Presentation/ViewModels/...` 表示 UI 控制器和状态
* `Presentation/Services/...` 表示 UI 服务 contracts 或导航类
* `Desktop Host/Diagnostics/...` 表示宿主生命周期诊断
* `Presentation/Diagnostics/...` 表示仅供 Presentation 使用的诊断辅助

不允许：

* 因为引用方便，把 Host 专用类放进 `Presentation`
* 因为构建通过，就把跨层公共类塞进任意现有项目

### 4.2 命名空间必须与物理位置一致

要求：

* `src/SimsModDesktop/Diagnostics/...` 使用 `SimsModDesktop.Diagnostics`
* `src/SimsModDesktop.Presentation/Diagnostics/...` 使用 `SimsModDesktop.Presentation.Diagnostics`
* 命名空间不要伪装成另一个项目的命名空间

禁止：

* 文件位于 `Presentation/...`，命名空间却声明成 `SimsModDesktop.Diagnostics`
* 同目录下相近职责的类型使用两套不同前缀，导致归属不清

### 4.3 可见性默认最小化

默认规则：

* 同项目内部使用：`internal`
* 只有确实需要跨程序集访问时才提升到 `public`
* 不能为了“方便跨项目引用”就把实现类改成 `public`

跨程序集访问前必须先问：

* 这个类型真的属于当前项目吗？
* 如果属于别的层，是不是文件放错地方了？
* 如果是共享能力，是不是应该抽 contracts/独立项目？

---

## 5. View、ViewModel、Controller 责任边界

### 5.1 View 代码隐藏

View 代码隐藏仅承担：

* Avalonia 事件桥接
* 焦点处理
* 可视树相关一次性钩子
* 将 UI 事件转发到 ViewModel/Command

不要在 View 代码隐藏中加入：

* 业务分支
* 复杂状态机
* 数据访问

### 5.2 MainWindowViewModel

`MainWindowViewModel` 当前仍是聚合型 VM。

规则：

* 新增复杂流程优先放到已有 Controller
* 不要继续把大型逻辑直接堆回 `MainWindowViewModel`
* 通过 Host 对象把 UI 操作面暴露给 Controller，而不是让 Controller直接持有 UI 细节对象

### 5.3 Presentation Controllers

适合放在 Controller 的内容：

* 需要协调多个 panel/workspace 状态
* 需要与 Application/Engine 交互
* 需要管理 UI 流程状态（busy、progress、log）

不适合：

* 与具体 View 事件强绑定的控件行为
* 跨层可复用的纯业务算法

---

## 6. DI 与组合规则

### 6.1 注册位置

* `Application` 自己在 `ApplicationServiceRegistration`
* `Presentation` 自己在 `PresentationServiceRegistration`
* `Infrastructure` 自己在 `Infrastructure.ServiceRegistration`
* `Desktop Host` 只做组合根与壳层适配

### 6.2 构造函数依赖

规则：

* 构造函数参数应体现真实依赖，不做 service locator
* 优先注入接口，其次注入明确归属的控制器/状态对象
* 新增 `ILogger<T>` 是允许的，但不要把 logging helper 本身变成跨层 service，除非有明确复用价值

### 6.3 测试兼容

凡是修改构造函数签名：

* 必须同步修复 `Tests` 中的手工 `new`
* 默认使用 `NullLogger<T>.Instance`
* 不允许留下“生产可构建、测试构造器已失效”的状态

---

## 7. 日志与诊断规范

### 7.1 结构化日志

使用原则：

* `ILogger<T>` 用于结构化日志
* `MainWindowStatusController` / `IUiLogSink` 用于 UI 文本日志
* 两者可以并存，但职责不同

建议：

* 控制台/诊断日志承载结构化字段
* UI 日志承载用户可读的摘要文本

### 7.2 诊断代码放置

放置规则：

* 宿主生命周期诊断：放在 `Desktop Host/Diagnostics`
* Presentation 流程耗时打点：放在 `Presentation/Diagnostics`
* 不要把 Host 和 Presentation 的诊断 helper 混放在同一项目
* 两类诊断实现默认都应保持 `internal`，除非未来确实抽成稳定共享 API

示例：

* 启动里程碑、首屏可见：属于 Host
* `toolkit.execute`、`traypreview.load`、`texture.compress`：属于 Presentation 控制器流程

### 7.3 日志级别

默认约定：

* 成功与开始：`Information`
* 取消：`Warning`
* 失败：`Error`

高频过程：

* 不要逐项刷日志
* 优先记录阶段汇总和总耗时

---

## 8. 新功能落点规则

新增功能时，优先按以下顺序决定代码归属：

1. 先判断是否是纯 UI 状态
2. 再判断是否是 UI 流程控制
3. 再判断是否是应用层用例编排
4. 最后判断是否是基础设施实现

快速判断：

* 依赖 `Avalonia`，通常不应进入 `Application` / `Infrastructure`
* 依赖文件系统/OS/API 细节，通常不应进入 `Presentation`
* 依赖 typed plans/contracts，但不关心 UI，通常应在 `Application`

---

## 9. 测试规范

### 9.1 修改行为必须补测试

以下变更必须补或更新测试：

* 构造函数依赖变更
* 执行流程状态变更
* 日志输出格式变更
* 生命周期/初始化路径变更

### 9.2 测试范围

优先级：

* 先补行为测试
* 再补边界测试
* 最后补集成链路测试

对于 UI 控制器：

* 允许通过 `LogText` / 状态字段做断言
* 不要求为每个结构化 `ILogger` 单独造测试 logger，除非该日志本身是业务契约

### 9.3 架构回归

如果新增了容易越层的模式，应该补架构守卫测试，至少覆盖：

* 禁止 `Application -> Presentation`
* 禁止旧路由/旧脚本类型回流
* 禁止新的层间反向依赖

---

## 10. 文档与评审要求

### 10.1 需要更新文档的情况

以下变更应同步更新 docs：

* 新的层级规则
* 新的公共约束
* 新的模块接入方式
* 会影响后续新增功能落点的结构性调整

### 10.2 Code Review 必查项

评审时至少检查：

* 文件放置位置是否与职责一致
* 命名空间是否与目录一致
* 是否出现为了跨项目访问而提升 `public`
* 是否引入反向依赖
* 是否让 `MainWindowViewModel` 继续膨胀
* 是否同步修复测试构造器

---

## 11. 常见反例

以下做法视为不符合当前规范：

* 把只属于 `Program/App/Window` 启动链路的类型放在 `Presentation`
* 文件在 `Presentation` 目录下，却声明为 `SimsModDesktop.Diagnostics`
* 为了让另一个项目调用，把本应 `internal` 的实现类升成 `public`
* 在 `View` 代码隐藏里编排业务流程
* 在 `Application` 里直接依赖 Avalonia 或具体 ViewModel

---

## 12. 变更前快速自检清单

提交前请至少自检一次：

* 这个类型是否放在了最自然的层里？
* 如果跨程序集访问，是真的共享能力，还是放错目录？
* 命名空间是否和文件路径一致？
* 新依赖是否让方向倒置？
* 是否需要同步更新测试与文档？

---

## 13. 当前默认结论

在本仓库当前阶段，优先遵循以下默认值：

* Desktop Host 承担入口、窗口生命周期和宿主级诊断
* Presentation 承担 ViewModel、UI 控制器、UI 流程日志与 timing
* Application 承担 typed plan / validation / orchestration
* Infrastructure 承担具体实现
* 新增代码默认最小可见性
* 目录归属与命名空间归属必须一致

如果某次实现必须偏离这些默认值，应在 PR 或文档中明确写出原因，而不是隐式放过。
