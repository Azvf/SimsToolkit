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
