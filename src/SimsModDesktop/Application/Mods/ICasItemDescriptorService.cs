using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.Mods;

public interface ICasItemDescriptorService
{
    IReadOnlyList<ModIndexedItemRecord> BuildCasItems(
        string packagePath,
        DbpfPackageIndex index,
        IReadOnlyList<ModPackageTextureCandidate> textureCandidates,
        FileInfo fileInfo,
        long nowUtcTicks);
}
