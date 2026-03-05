using SimsModDesktop.PackageCore;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class PathIdentityResolverTests
{
    [Fact]
    public void ResolveDirectory_ExistingPath_ReturnsCanonicalAndExists()
    {
        using var temp = new TempDirectory("path-resolver-existing");
        var resolver = new SystemPathIdentityResolver();

        var resolved = resolver.ResolveDirectory(temp.Path + Path.DirectorySeparatorChar);

        Assert.True(resolved.Exists);
        Assert.False(string.IsNullOrWhiteSpace(resolved.FullPath));
        Assert.False(string.IsNullOrWhiteSpace(resolved.CanonicalPath));
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(temp.Path)),
            resolved.CanonicalPath,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDirectory_MissingPath_DoesNotThrowAndFallsBackToFullPath()
    {
        using var temp = new TempDirectory("path-resolver-missing");
        var missingPath = Path.Combine(temp.Path, "missing");
        var resolver = new SystemPathIdentityResolver();

        var resolved = resolver.ResolveDirectory(missingPath);

        Assert.False(resolved.Exists);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(missingPath)),
            resolved.CanonicalPath,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EqualsDirectory_SameDirectoryDifferentRepresentations_ReturnsTrue()
    {
        using var temp = new TempDirectory("path-resolver-equals");
        var resolver = new SystemPathIdentityResolver();
        var left = temp.Path;
        var right = Path.Combine(temp.Path, ".", "sub", "..");

        var equals = resolver.EqualsDirectory(left, right);

        Assert.True(equals);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
