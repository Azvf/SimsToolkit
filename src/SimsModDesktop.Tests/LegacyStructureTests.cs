namespace SimsModDesktop.Tests;

public sealed class LegacyStructureTests
{
    [Fact]
    public void LegacyApplicationFolder_ContainsNoSourceFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        var legacyApplicationPath = Path.Combine(repositoryRoot, "src", "SimsModDesktop", "Application");

        var remainingSourceFiles = Directory.Exists(legacyApplicationPath)
            ? Directory.EnumerateFiles(legacyApplicationPath, "*.cs", SearchOption.AllDirectories).ToArray()
            : Array.Empty<string>();

        Assert.True(
            remainingSourceFiles.Length == 0,
            "Legacy application folder should be empty of source files:" + Environment.NewLine + string.Join(Environment.NewLine, remainingSourceFiles));
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
