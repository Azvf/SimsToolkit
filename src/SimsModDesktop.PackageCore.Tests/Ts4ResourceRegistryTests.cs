using SimsModDesktop.PackageCore;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class Ts4ResourceRegistryTests
{
    [Fact]
    public void Registry_ResolvesKnownKindsAndTypes()
    {
        var registry = new Ts4ResourceRegistry();

        Assert.True(registry.TryGetTypeId(Ts4ResourceKind.Rle2, out var rle2));
        Assert.Equal(Sims4ResourceTypeRegistry.Rle2, rle2);
        Assert.Equal(Ts4ResourceKind.Rle2, registry.ResolveKind(Sims4ResourceTypeRegistry.Rle2));

        Assert.True(registry.TryGetTypeId(Ts4ResourceKind.SimModifier, out var smod));
        Assert.Equal(Sims4ResourceTypeRegistry.SimModifier, smod);
        Assert.Equal(Ts4ResourceKind.SimModifier, registry.ResolveKind(Sims4ResourceTypeRegistry.SimModifier));
    }
}
