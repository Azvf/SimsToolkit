using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.Mods;

public interface IBuildBuyItemDescriptorService
{
    IReadOnlyList<ModIndexedItemRecord> BuildItems(
        string packagePath,
        DbpfPackageIndex index,
        IReadOnlyList<ModPackageTextureCandidate> textureCandidates,
        FileInfo fileInfo,
        long nowUtcTicks);
}
