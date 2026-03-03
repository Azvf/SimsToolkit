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
