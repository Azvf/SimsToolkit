# SimsToolkit 跨平台迁移实施计划

## 项目概述

本文档详细描述了将SimsToolkit从PowerShell依赖的Windows专用应用迁移为跨平台.NET应用的实施计划。

## 当前架构分析

### 技术栈现状
- **前端**: Avalonia UI (.NET 8.0) - ✅ 已跨平台
- **后端执行引擎**: PowerShell 7+ - ❌ Windows依赖
- **核心功能**: PowerShell脚本 - ❌ 需要重写
- **配置管理**: JSON + PowerShell - ⚠️ 部分依赖
- **文件操作**: Windows特定API - ❌ 需要替换

### 平台依赖分析
1. **PowerShell执行引擎** - 重度依赖
2. **Microsoft.VisualBasic.FileSystem** - 回收站功能
3. **Windows路径格式** - 硬编码路径分隔符
4. **注册表访问** - 游戏路径发现

## 迁移策略

### 阶段一: 架构抽象化 (2-3周)
**目标**: 建立跨平台抽象层，最小化业务逻辑变更

#### 1.1 创建执行引擎抽象接口
```csharp
// 新文件: Application/Execution/IExecutionEngine.cs
public interface IExecutionEngine
{
    Task<ExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        IProgress<ProgressUpdate> progress,
        CancellationToken cancellationToken);
    
    bool IsSupportedOnCurrentPlatform { get; }
    string EngineName { get; }
}
```

#### 1.2 实现PowerShell适配器
```csharp
// 新文件: Infrastructure/Execution/PowerShellExecutionEngine.cs
public class PowerShellExecutionEngine : IExecutionEngine
{
    // 包装现有的SimsPowerShellRunner
}
```

#### 1.3 创建跨平台文件操作服务
```csharp
// 新文件: Application/Services/IFileOperationService.cs
public interface IFileOperationService
{
    Task<bool> MoveToRecycleBinAsync(string path);
    Task<bool> DeleteFileAsync(string path, bool permanent = false);
    Task<bool> DeleteDirectoryAsync(string path, bool permanent = false);
    
    // 统一路径处理
    string NormalizePath(string path);
    string CombinePaths(params string[] paths);
}
```

### 阶段二: 核心服务实现 (4-6周)
**目标**: 实现关键功能的跨平台版本

#### 2.1 文件哈希服务
```csharp
// 新文件: Application/Services/IHashComputationService.cs
public interface IHashComputationService
{
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken);
    Task<string> ComputeFilePrefixHashAsync(string filePath, int prefixBytes, CancellationToken cancellationToken);
    Task<bool> AreFilesIdenticalAsync(string path1, string path2, CancellationToken cancellationToken);
}
```

#### 2.2 目录遍历和过滤服务
```csharp
// 新文件: Application/Services/IDirectoryTraversalService.cs
public interface IDirectoryTraversalService
{
    IAsyncEnumerable<FileInfo> EnumerateFilesAsync(
        string directoryPath, 
        string[] extensions,
        bool recursive,
        CancellationToken cancellationToken);
        
    Task<long> GetDirectorySizeAsync(string directoryPath, CancellationToken cancellationToken);
}
```

#### 2.3 冲突检测和解决引擎
```csharp
// 新文件: Application/Services/IConflictResolutionEngine.cs
public interface IConflictResolutionEngine
{
    Task<ConflictResolution> ResolveConflictAsync(
        FileConflict conflict,
        ConflictResolutionStrategy strategy,
        CancellationToken cancellationToken);
}
```

### 阶段三: 功能迁移 (6-8周)
**目标**: 逐个迁移核心功能到跨平台实现

#### 3.1 文件操作功能统一
创建统一的文件转换引擎：

```csharp
// 新文件: Application/Services/FileTransformationEngine.cs
public class FileTransformationEngine : IFileTransformationEngine
{
    public async Task<TransformationResult> TransformAsync(
        TransformationOptions options,
        TransformationMode mode,
        IProgress<ProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        return mode switch
        {
            TransformationMode.Flatten => await FlattenAsync(options, progress, cancellationToken),
            TransformationMode.Normalize => await NormalizeAsync(options, progress, cancellationToken),
            TransformationMode.Merge => await MergeAsync(options, progress, cancellationToken),
            TransformationMode.Organize => await OrganizeAsync(options, progress, cancellationToken),
            _ => throw new NotSupportedException($"Mode {mode} not supported")
        };
    }
}
```

#### 3.2 配置管理系统重构
```csharp
// 新文件: Application/Configuration/IConfigurationProvider.cs
public interface IConfigurationProvider
{
    Task<T> GetConfigurationAsync<T>(string key, CancellationToken cancellationToken);
    Task SetConfigurationAsync<T>(string key, T value, CancellationToken cancellationToken);
    Task<bool> IsPlatformSpecificAsync(string key, CancellationToken cancellationToken);
}
```

### 阶段四: 测试和优化 (2-3周)
**目标**: 建立完善的跨平台测试体系

#### 4.1 多平台测试策略
- **单元测试**: 每个服务独立测试
- **集成测试**: 跨平台功能验证
- **性能测试**: 大文件处理性能对比
- **兼容性测试**: Windows/Linux/macOS验证

#### 4.2 性能优化
- 内存使用优化
- 并行处理优化
- 缓存策略改进
- I/O操作优化

## 实施时间表

| 阶段 | 任务 | 预计时间 | 依赖关系 |
|-----|------|---------|----------|
| 阶段一 | 架构抽象化 | 2-3周 | 无 |
| 阶段二 | 核心服务实现 | 4-6周 | 阶段一完成 |
| 阶段三 | 功能迁移 | 6-8周 | 阶段二完成 |
| 阶段四 | 测试和优化 | 2-3周 | 阶段三完成 |
| **总计** | **完整迁移** | **14-20周** | **3.5-5个月** |

## 风险评估和缓解策略

### 高风险项目
1. **PowerShell脚本重写复杂性**
   - 缓解: 保持现有脚本作为后备方案
   - 策略: 渐进式替换，功能级别验证

2. **跨平台文件系统差异**
   - 缓解: 抽象文件操作系统
   - 策略: 平台特定实现，统一接口

3. **性能回归风险**
   - 缓解: 性能基准测试
   - 策略: 并行运行对比，逐步优化

### 中等风险项目
1. **依赖注入重构**
   - 缓解: 分模块逐步迁移
   - 策略: 保持向后兼容性

2. **配置格式变更**
   - 缓解: 配置迁移工具
   - 策略: 支持旧格式读取

## 质量保证措施

### 代码质量标准
- **代码覆盖率**: >80%单元测试覆盖
- **代码审查**: 每个PR需要至少1人审查
- **静态分析**: 使用Roslyn分析器
- **性能基准**: 关键路径性能监控

### 测试策略
1. **自动化测试**: CI/CD管道集成
2. **多平台测试**: GitHub Actions多平台运行
3. **回归测试**: PowerShell与C#版本结果对比
4. **用户验收测试**: Beta版本用户反馈

### 监控和度量
- **性能指标**: 执行时间、内存使用、CPU占用
- **稳定性指标**: 崩溃率、错误率、恢复能力
- **用户满意度**: 功能完整性、易用性评价

## 成功标准

### 技术指标
- [ ] 所有核心功能跨平台可用
- [ ] 性能不低于原有PowerShell实现
- [ ] 零平台特定崩溃
- [ ] 完整的向后兼容性

### 业务指标
- [ ] 支持Windows/Linux/macOS三平台
- [ ] 用户迁移率>90%
- [ ] 新功能开发效率提升>50%
- [ ] 维护成本降低>30%

## 后续优化方向

### 短期优化（迁移完成后3个月）
1. **插件化架构**: 支持第三方功能扩展
2. **云端集成**: 支持云存储和同步
3. **性能调优**: 基于用户数据的针对性优化

### 长期规划（迁移完成后6-12个月）
1. **AI辅助**: 智能Mod推荐和冲突预测
2. **社区功能**: 用户分享和评价系统
3. **移动端**: 配套移动应用开发

---

**文档版本**: 1.0  
**创建日期**: 2024年3月  
**最后更新**: 2024年3月  
**作者**: 架构分析团队