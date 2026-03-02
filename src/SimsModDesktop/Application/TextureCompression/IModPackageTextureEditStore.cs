namespace SimsModDesktop.Application.TextureCompression;

public interface IModPackageTextureEditStore
{
    Task SaveAsync(ModPackageTextureEditRecord record, CancellationToken cancellationToken = default);

    Task<ModPackageTextureEditRecord?> TryGetLatestActiveEditAsync(
        string packagePath,
        string resourceKeyText,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModPackageTextureEditRecord>> GetHistoryAsync(
        string packagePath,
        string resourceKeyText,
        int maxCount = 10,
        CancellationToken cancellationToken = default);

    Task MarkRolledBackAsync(
        string editId,
        CancellationToken cancellationToken = default);
}
