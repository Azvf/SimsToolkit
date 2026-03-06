namespace SimsModDesktop.PackageCore;

public interface ITS4ResourceLocator
{
    Ts4ResourceResolution Resolve(DbpfCatalogSnapshot snapshot, DbpfResourceKey key, ResourceLookupPolicy policy = ResourceLookupPolicy.PreferModsSdxGame);

    IReadOnlyList<ResourceLocation> Find(DbpfCatalogSnapshot snapshot, DbpfResourceKey key, ResourceLookupPolicy policy = ResourceLookupPolicy.PreferModsSdxGame);

    bool TryResolveFirst(DbpfCatalogSnapshot snapshot, DbpfResourceKey key, out ResourceLocation location, ResourceLookupPolicy policy = ResourceLookupPolicy.PreferModsSdxGame);
}
