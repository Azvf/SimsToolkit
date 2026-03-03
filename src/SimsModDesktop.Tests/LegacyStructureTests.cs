namespace SimsModDesktop.Tests;

public sealed class LegacyStructureTests
{
    [Fact]
    public void LegacyApplicationFolder_ContainsNoSourceFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        var legacyApplicationPath = Path.Combine(repositoryRoot, "src", "SimsModDesktop", "Application");
        Assert.False(
            Directory.Exists(legacyApplicationPath),
            $"Legacy application folder should not exist: {legacyApplicationPath}");
    }

    [Fact]
    public void LegacyServicesFolder_DoesNotExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var legacyServicesPath = Path.Combine(repositoryRoot, "src", "SimsModDesktop", "Services");
        Assert.False(
            Directory.Exists(legacyServicesPath),
            $"Legacy services folder should not exist: {legacyServicesPath}");
    }

    [Fact]
    public void DuplicateShellProjectFolder_DoesNotExist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var duplicateShellProjectPath = Path.Combine(repositoryRoot, "src", "SimsModDesktop.App");
        Assert.False(
            Directory.Exists(duplicateShellProjectPath),
            $"Duplicate shell project folder should not exist: {duplicateShellProjectPath}");
    }

    [Fact]
    public void Solution_DoesNotContain_DuplicateShellProject()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionPath = Path.Combine(repositoryRoot, "SimsDesktopTools.sln");
        var solutionContents = File.ReadAllText(solutionPath);

        Assert.DoesNotContain(@"""SimsModDesktop.App"", ""src\SimsModDesktop.App\SimsModDesktop.App.csproj""", solutionContents, StringComparison.Ordinal);
    }

    [Fact]
    public void AppBootstrap_UsesSingleShellCompositionEntryPoint()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appBootstrapPath = Path.Combine(repositoryRoot, "src", "SimsModDesktop", "App.axaml.cs");
        var appBootstrapSource = File.ReadAllText(appBootstrapPath);

        Assert.Contains(".AddSimsDesktopShell()", appBootstrapSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".AddSimsDesktopInfrastructure()", appBootstrapSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".AddSimsDesktopExecution()", appBootstrapSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".AddSimsDesktopModules()", appBootstrapSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".AddSimsDesktopPresentation()", appBootstrapSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyCompositionMethods_AreNotPublicApis()
    {
        var shellCompositionType = typeof(SimsModDesktop.App).Assembly.GetType("SimsModDesktop.Composition.ServiceCollectionExtensions");
        Assert.NotNull(shellCompositionType);

        var publicMethodNames = shellCompositionType!
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();

        Assert.Contains("AddSimsDesktopShell", publicMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("AddSimsDesktopInfrastructure", publicMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("AddSimsDesktopExecution", publicMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("AddSimsDesktopModules", publicMethodNames, StringComparer.Ordinal);
        Assert.DoesNotContain("AddSimsDesktopPresentation", publicMethodNames, StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SimsDesktopTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
