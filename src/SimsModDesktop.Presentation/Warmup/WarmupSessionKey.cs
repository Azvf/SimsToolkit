using SimsModDesktop.Application.Caching;

namespace SimsModDesktop.Presentation.Warmup;

internal readonly record struct WarmupSessionKey(
    CacheWarmupDomain Domain,
    string SourceKey,
    string? VariantKey = null,
    string? VersionToken = null)
{
    public static WarmupSessionKey ForModsRoot(string sourceKey, long inventoryVersion) =>
        new(CacheWarmupDomain.ModsCatalog, sourceKey, VersionToken: inventoryVersion.ToString());

    public static WarmupSessionKey ForTrayRoot(string sourceKey, long inventoryVersion) =>
        new(CacheWarmupDomain.TrayDependency, sourceKey, VersionToken: inventoryVersion.ToString());

    public static WarmupSessionKey ForSaveDescriptor(string sourceKey, string versionToken) =>
        new(CacheWarmupDomain.SavePreviewDescriptor, sourceKey, VersionToken: versionToken);

    public static WarmupSessionKey ForSaveArtifact(string sourceKey, string householdKey, string versionToken) =>
        new(CacheWarmupDomain.SavePreviewArtifact, sourceKey, householdKey, versionToken);

    public override string ToString()
    {
        return string.Join("|", Domain, SourceKey, VariantKey ?? string.Empty, VersionToken ?? string.Empty);
    }
}
