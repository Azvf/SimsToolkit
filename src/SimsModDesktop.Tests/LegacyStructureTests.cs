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
    public void LegacyServicesFolder_OnlyContainsTrayDependenciesLauncher()
    {
        var repositoryRoot = FindRepositoryRoot();
        var legacyServicesPath = Path.Combine(repositoryRoot, "src", "SimsModDesktop", "Services");

        var remainingSourceFiles = Directory.Exists(legacyServicesPath)
            ? Directory.EnumerateFiles(legacyServicesPath, "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(legacyServicesPath, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        Assert.Equal(["TrayDependenciesLauncher.cs"], remainingSourceFiles);
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
