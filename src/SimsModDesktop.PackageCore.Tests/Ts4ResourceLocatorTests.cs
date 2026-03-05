using System.Collections.Frozen;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class Ts4ResourceLocatorTests
{
    [Fact]
    public void TryResolveFirst_PrefersModsThenContentThenGame()
    {
        var key = new DbpfResourceKey(Sims4ResourceTypeRegistry.CasPart, 0, 0x1234UL);
        var locations = new[]
        {
            new ResourceLocation("C:\\Games\\The Sims 4\\Data\\Client.package", 0, MakeEntry(key)),
            new ResourceLocation("C:\\Users\\Test\\Documents\\Electronic Arts\\The Sims 4\\content\\foo.package", 1, MakeEntry(key)),
            new ResourceLocation("C:\\Users\\Test\\Documents\\Electronic Arts\\The Sims 4\\Mods\\bar.package", 2, MakeEntry(key))
        };

        var snapshot = new DbpfCatalogSnapshot
        {
            RootPath = "C:\\",
            Packages = Array.Empty<DbpfPackageIndex>(),
            ExactIndex = new Dictionary<DbpfResourceKey, ResourceLocation[]> { [key] = locations }.ToFrozenDictionary(),
            TypeInstanceIndex = new Dictionary<TypeInstanceKey, ResourceLocation[]>().ToFrozenDictionary(),
            SupportedInstanceIndex = new Dictionary<ulong, ResourceLocation[]>().ToFrozenDictionary(),
            Issues = Array.Empty<DbpfCatalogIssue>()
        };

        var locator = new Ts4ResourceLocator();

        Assert.True(locator.TryResolveFirst(snapshot, key, out var location, ResourceLookupPolicy.PreferModsSdxGame));
        Assert.Contains("\\Mods\\", location.FilePath, StringComparison.OrdinalIgnoreCase);

        Assert.True(locator.TryResolveFirst(snapshot, key, out location, ResourceLookupPolicy.PreferGameSdxMods));
        Assert.Contains("\\Data\\", location.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolveFirst_FallsBackToTypeInstanceWhenExactKeyMissing()
    {
        var requestedKey = new DbpfResourceKey(Sims4ResourceTypeRegistry.CasPart, 0, 0x1234UL);
        var actualKey = new DbpfResourceKey(Sims4ResourceTypeRegistry.CasPart, 0xDEADBEEF, 0x1234UL);
        var location = new ResourceLocation(
            "/home/test/Documents/Electronic Arts/The Sims 4/Mods/bar.package",
            0,
            MakeEntry(actualKey));

        var snapshot = new DbpfCatalogSnapshot
        {
            RootPath = "/",
            Packages = Array.Empty<DbpfPackageIndex>(),
            ExactIndex = new Dictionary<DbpfResourceKey, ResourceLocation[]>().ToFrozenDictionary(),
            TypeInstanceIndex = new Dictionary<TypeInstanceKey, ResourceLocation[]>
            {
                [new TypeInstanceKey(requestedKey.Type, requestedKey.Instance)] = [location]
            }.ToFrozenDictionary(),
            SupportedInstanceIndex = new Dictionary<ulong, ResourceLocation[]>().ToFrozenDictionary(),
            Issues = Array.Empty<DbpfCatalogIssue>()
        };

        var locator = new Ts4ResourceLocator();

        Assert.True(locator.TryResolveFirst(snapshot, requestedKey, out var resolved, ResourceLookupPolicy.PreferModsSdxGame));
        Assert.Equal(actualKey.Group, resolved.Entry.Group);
        Assert.Equal(actualKey.Instance, resolved.Entry.Instance);
    }

    private static DbpfIndexEntry MakeEntry(DbpfResourceKey key)
    {
        return new DbpfIndexEntry(key.Type, key.Group, key.Instance, 0, 10, 10, 0, false);
    }
}
