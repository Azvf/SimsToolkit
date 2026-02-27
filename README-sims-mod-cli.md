# Sims Mod CLI

统一入口脚本：`sims-mod-cli.ps1`  
模块文件：`modules/SimsModToolkit.psm1`
核心共享逻辑：`modules/SimsFileOpsCore.psm1`（冲突判定、哈希缓存、并行前缀哈希、扩展名集合）
参数注入共享逻辑：`modules/SimsInvokeUtils.psm1`（参数透传、WhatIf/Confirm 统一处理）

独立测试模块：`modules/SimsTrayDependencyProbe.psm1`（Tray -> 可能依赖的 package 候选探针）
独立测试脚本：`tray-mod-dependency-probe.ps1`

## 仓库目录约定

- 测试资产统一放在：`testing/assets/`
- 软件功能输出（如 CSV、导出包）统一放在：`output/`
- 构建产物保持默认位置（例如 `src/.../bin`、`src/.../obj`）

## 功能

1. `organize`：批量处理来源项（压缩包 + 可选散文件/散目录），迁移 Mods/Tray（默认按来源项名落到独立目录；同名按日期覆盖，支持保留源压缩包）
2. `flatten`：默认将每个一级目录内的深层文件扁平化到该一级目录下；可选直接扁平到根目录
3. `normalize`：规范一级目录命名并在重名时合并
4. `merge`：将多个目录合并到单一目录（如 `General Character CC`），同名按日期保留更新版本
5. `trayprobe`：Tray 反向依赖分析（支持 `StrictS4TI` / `Legacy`，未引用包统计、导出与列表）

## 用法

```powershell
# 1) 批量迁移压缩包（默认每个压缩包 -> ModsRoot 下独立目录）
& .\sims-mod-cli.ps1 organize `
  -SourceDir 'g:\Download\sims\testing\assets' `
  -ModsRoot 'J:\Sims Mods\The Sims 4\Mods\[人物美化]' `
  -TrayRoot 'J:\Sims Mods\The Sims 4\Tray' `
  -KeepZip
```

```powershell
# 1b) 如需统一写入单一目录，显式指定 -UnifiedModsFolder
& .\sims-mod-cli.ps1 organize `
  -SourceDir 'g:\Download\sims\testing\assets' `
  -ModsRoot 'J:\Sims Mods\The Sims 4\Mods\[人物美化]' `
  -UnifiedModsFolder 'General 人物CC' `
  -TrayRoot 'J:\Sims Mods\The Sims 4\Tray'
```

```powershell
# 1c) 非规范源（散文件/散目录 + 子目录压缩包）容错迁移（预演）
& .\sims-mod-cli.ps1 organize `
  -SourceDir 'G:\Download\sims mods' `
  -ModsRoot 'J:\Sims Mods\The Sims 4\Mods\[房屋家建]' `
  -UnifiedModsFolder '!Mod资源' `
  -TrayRoot 'J:\Sims Mods\The Sims 4\Tray' `
  -RecurseSource $true `
  -IncludeLooseSources $true `
  -VerifyContentOnNameConflict `
  -KeepZip `
  -WhatIf
```

```powershell
# 2) 扁平化目录（预演）
& .\sims-mod-cli.ps1 flatten `
  -FlattenRootPath 'E:\Documents\Electronic Arts\The Sims 4\Mods\[人物美化]\General 人物CC' `
  -WhatIf
```

```powershell
# 2b) 仅扁平化 Mod 文件（图片/文档保持原位）
& .\sims-mod-cli.ps1 flatten `
  -FlattenRootPath 'J:\Sims Mods\The Sims 4\Mods\[房屋家建]' `
  -ModFilesOnly `
  -ModExtensions '.package','.ts4script' `
  -WhatIf
```

```powershell
# 2c) 将所有子目录文件直接扁平到 Root（即 Root 本身）
& .\sims-mod-cli.ps1 flatten `
  -FlattenRootPath 'J:\Sims Mods\The Sims 4\Mods' `
  -FlattenToRoot `
  -WhatIf
```

```powershell
# 3) 规范目录名
& .\sims-mod-cli.ps1 normalize `
  -NormalizeRootPath 'J:\Sims Mods\The Sims 4\Mods\[人物美化]\General 人物CC'
```

```powershell
# 4) 合并到单一目录
& .\sims-mod-cli.ps1 merge `
  -MergeSourcePaths 'g:\Download\sims\testing\assets\General 人物CC','g:\Download\sims\testing\assets\Migrated' `
  -MergeTargetPath 'J:\Sims Mods\The Sims 4\Mods2\[人物美化]\General Character CC'
```

```powershell
# 5) 启用加速内容校验（仅预热潜在重名冲突文件：大小 + 前100KB MD5 命中后再整文件 MD5）
& .\sims-mod-cli.ps1 merge `
  -MergeSourcePaths 'A','B' `
  -MergeTargetPath 'C' `
  -ModFilesOnly `
  -VerifyContentOnNameConflict `
  -PrefixHashBytes 102400 `
  -HashWorkerCount 8
```

```powershell
# 6) Tray 依赖分析（通过统一入口 trayprobe）
& .\sims-mod-cli.ps1 trayprobe `
  -TrayPath 'J:\Sims Mods\The Sims 4\Tray' `
  -ModsPath 'J:\Sims Mods\The Sims 4\Mods\[房屋家建]\!Mod资源' `
  -AnalysisMode StrictS4TI `
  -S4tiPath 'D:\SIM4\Sims 4 Tray Importer (S4TI)' `
  -ProbeWorkerCount 8 `
  -TopN 200 `
  -ExportUnusedPackages
```

## 参数说明

- 通用：
  - `-WhatIf`：预演，不落盘
  - `-Confirm`：交互确认
- `organize`：
  - `-SourceDir`
  - `-ZipNamePattern`
  - `-ModsRoot`
  - `-UnifiedModsFolder`（可选；不填时按压缩包名创建独立目录）
  - `-TrayRoot`
  - `-KeepZip`
  - `-RecurseSource`（默认 `true`；递归扫描子目录中的压缩包）
  - `-IncludeLooseSources`（默认 `true`；处理 SourceDir 下非压缩包的散文件/散目录）
  - `-ModExtensions`（默认 `.package,.ts4script`；用于非规范源回退识别）
  - `-VerifyContentOnNameConflict`（文件源冲突时启用哈希比对，避免误覆盖）
  - `-PrefixHashBytes`（默认 `102400`；内容比对前缀哈希大小）
  - 脚本会在汇总末尾对检测到的 `.ts4script` 输出 `Warning`
  - 脚本会自动净化非法路径字符（如 `|:*?"<>`），并在汇总末尾输出 `sanitized-path` 警告样本
- `flatten`：
  - `-FlattenRootPath`
  - `-FlattenToRoot`（可选；启用后把所有子目录文件扁平到 Root 本身）
  - `-SkipPruneEmptyDirs`
  - `-ModFilesOnly`
  - `-ModExtensions`
  - `-VerifyContentOnNameConflict`
  - `-PrefixHashBytes`
  - `-HashWorkerCount`（默认 8）
- `normalize`：
  - `-NormalizeRootPath`
- `merge`：
  - `-MergeSourcePaths`（必填，支持多个）
  - `-MergeTargetPath`
  - `-SkipPruneEmptyDirs`
  - `-ModFilesOnly`
  - `-ModExtensions`
  - `-VerifyContentOnNameConflict`
  - `-PrefixHashBytes`
  - `-HashWorkerCount`（默认 8）
- `trayprobe`（参数与 `tray-mod-dependency-probe.ps1` 基本一致）：
  - `-TrayPath`
  - `-ModsPath`
  - `-TrayItemKey`
  - `-AnalysisMode`（`StrictS4TI` / `Legacy`）
  - `-S4tiPath`
  - `-ListTrayItems` / `-ListTopN`
  - `-PreviewTrayItems` / `-PreviewTopN` / `-PreviewFilesPerItem`
  - `-MinMatchCount` / `-TopN` / `-MaxPackageCount` / `-ProbeWorkerCount`（默认 8）
  - `-ExportUnusedPackages` / `-UnusedOutputCsv`
  - `-ExportMatchedPackages` / `-ExportTargetPath` / `-ExportMinConfidence`
  - `-OutputCsv`

## 进度输出（供 GUI 解析）

脚本会输出结构化进度行（stdout）：

`##SIMS_PROGRESS##|<stage>|<current>|<total>|<percent>|<detail>`

## Tray 依赖探针（独立）

说明：下列命令可直接运行独立脚本；同等能力也可通过统一入口 `sims-mod-cli.ps1 trayprobe ...` 调用。

```powershell
& .\tray-mod-dependency-probe.ps1 `
  -TrayPath 'J:\Sims Mods\The Sims 4\Tray' `
  -ModsPath 'J:\Sims Mods\The Sims 4\Mods\[房屋家建]\!Mod资源' `
  -AnalysisMode StrictS4TI `
  -S4tiPath 'D:\SIM4\Sims 4 Tray Importer (S4TI)' `
  -ProbeWorkerCount 8 `
  -MinMatchCount 1 `
  -TopN 120
```

```powershell
# 先列可选 TrayItemKey（按实例ID分组，更接近一个完整预设）
& .\tray-mod-dependency-probe.ps1 `
  -TrayPath 'J:\Sims Mods\The Sims 4\Tray' `
  -ListTrayItems `
  -ListTopN 200
```

```powershell
# 仅分析单个 Tray 项（降低噪声）
& .\tray-mod-dependency-probe.ps1 `
  -TrayPath 'J:\Sims Mods\The Sims 4\Tray' `
  -ModsPath 'J:\Sims Mods\The Sims 4\Mods\[房屋家建]\!Mod资源' `
  -TrayItemKey '0x1234567890ABCDEF' `
  -AnalysisMode StrictS4TI `
  -S4tiPath 'D:\SIM4\Sims 4 Tray Importer (S4TI)' `
  -MinMatchCount 1 `
  -TopN 120
```

```powershell
# 同时导出“未被 Tray 命中”的 package（MatchInstanceCount=0）
& .\tray-mod-dependency-probe.ps1 `
  -TrayPath 'J:\Sims Mods\The Sims 4\Tray' `
  -ModsPath 'J:\Sims Mods\The Sims 4\Mods\[房屋家建]' `
  -ExportUnusedPackages `
  -UnusedOutputCsv 'G:\Download\sims\output\tray_unused_housebuild.csv'
```

```powershell
# 生成离线 Tray 预览（CSV + HTML 卡片页）
& .\tray-mod-dependency-probe.ps1 `
  -TrayPath 'J:\Sims Mods\The Sims 4\Tray' `
  -PreviewTrayItems `
  -PreviewTopN 500 `
  -PreviewFilesPerItem 12 `
  -PreviewOutputHtml 'G:\Download\sims\output\tray_preview.html' `
  -PreviewOutputCsv 'G:\Download\sims\output\tray_preview.csv'
```

提示：
- `AnalysisMode` 默认是 `StrictS4TI`（基于 S4TI 的 Tray 解析 + TGI 精确匹配）；旧的启发式逻辑可用 `-AnalysisMode Legacy` 回退。
- `StrictS4TI` 需要本地 S4TI 安装目录（默认 `D:\SIM4\Sims 4 Tray Importer (S4TI)`），可用 `-S4tiPath` 覆盖。
- `StrictS4TI` 现会额外提取家庭相关 Tray 文件（`.householdbinary/.hhi/.sgi`）中的实例候选，降低人物美化/CAS 包在“仅家庭引用”场景下被误判为未引用的概率。
- `TrayItemKey` 现在支持两种写法：`0x1234567890ABCDEF`（推荐）或旧格式 `0x00000000!0x1234567890ABCDEF`。
- `-PreviewTrayItems` 为纯离线预览模式，不依赖联网；支持 `-TrayItemKey` 仅看单个预设。
- 预览输出字段 `PresetType` 为规则推断（`Lot` / `Room` / `Household` / `Mixed` / `GenericTray` / `Unknown`）。
- `RawScanStep` 默认 `0`（关闭原始字节扫描，减少误报）；需要更激进召回时可设为 `4`。
- `CandidateMinFrequency` / `RawScanStep` 仅对 `Legacy` 模式有效。
- `ProbeWorkerCount` 默认 `8`；当机器磁盘较慢或并行过高导致抖动时，可降到 `4` 或 `1`。
- 若不显式指定 `-OutputCsv`，CSV 默认输出到 `output/`。
- 若不显式指定 `-PreviewOutputCsv` 或 `-PreviewOutputHtml`，文件默认输出到 `output/`。
- 若启用 `-ExportMatchedPackages` 且未指定 `-ExportTargetPath`，导出目录默认在 `output/` 下。
- 若启用 `-ExportUnusedPackages` 且未指定 `-UnusedOutputCsv`，未使用清单 CSV 默认输出到 `output/` 下。
- `organize` 不指定 `-UnifiedModsFolder` 时，会按来源项分目录写入（不会全部混在一个目录里）。
