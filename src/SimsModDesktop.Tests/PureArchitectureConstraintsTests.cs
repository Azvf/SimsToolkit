using System.Reflection;
using SimsModDesktop.Application.Execution;

namespace SimsModDesktop.Tests;

public sealed class PureArchitectureConstraintsTests
{
    [Fact]
    public void ApplicationAssembly_DoesNotContain_LegacyPlanningOrScriptExecutionTypes()
    {
        var assembly = typeof(IToolkitActionPlanner).Assembly;
        var applicationTypeNames = assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("SimsModDesktop.Application", StringComparison.Ordinal) == true)
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.DoesNotContain(applicationTypeNames, name => name.Contains("ActionModule", StringComparison.Ordinal));
        Assert.DoesNotContain(applicationTypeNames, name => name.Contains("ActionModuleRegistry", StringComparison.Ordinal));
        Assert.DoesNotContain(applicationTypeNames, name => name.Contains("MainWindowPlanBuilder", StringComparison.Ordinal));
        Assert.DoesNotContain(applicationTypeNames, name => name.Contains("ToolkitExecutionRunner", StringComparison.Ordinal));
        Assert.DoesNotContain(applicationTypeNames, name => name.Contains("TrayPreviewRunner", StringComparison.Ordinal));
        Assert.DoesNotContain(applicationTypeNames, name => name.Contains("ActionExecutionStrategy", StringComparison.Ordinal));
        Assert.DoesNotContain(applicationTypeNames, name => name.Contains("ExecutionEngineRoutingPolicy", StringComparison.Ordinal));
        Assert.DoesNotContain(applicationTypeNames, name => name.Contains("SimsCliArgumentBuilder", StringComparison.Ordinal));
        Assert.DoesNotContain(applicationTypeNames, name => name.Contains("IActionCliArgumentMapper", StringComparison.Ordinal));
        Assert.DoesNotContain(applicationTypeNames, name => name.Contains("IExecutionOutputParser", StringComparison.Ordinal));
    }

    [Fact]
    public void RemovedLegacyExecutionFiles_DoNotExist()
    {
        var repoRoot = FindRepoRoot();

        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "IToolkitExecutionRunner.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "ToolkitExecutionRunner.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "ITrayPreviewRunner.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "TrayPreviewRunner.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "IMainWindowPlanBuilder.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "MainWindowPlanBuilder.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "ActionExecutionStrategy.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "IActionExecutionStrategy.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "ExecutionEngineRoutingPolicy.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "ISimsPowerShellRunner.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Execution", "IExecutionEngine.cs")));
        Assert.False(Directory.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Application", "Cli")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Infrastructure", "Execution", "PowerShellExecutionEngine.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "SimsModDesktop.Infrastructure", "Execution", "SimsPowerShellRunner.cs")));
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "SimsDesktopTools.sln")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
