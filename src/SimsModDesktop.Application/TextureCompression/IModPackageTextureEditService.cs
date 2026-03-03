namespace SimsModDesktop.Application.TextureCompression;

public interface IModPackageTextureEditService
{
    Task<ModPackageTexturePreviewResult> PreviewAsync(
        string packagePath,
        ModPackageTextureCandidate candidate,
        CancellationToken cancellationToken = default);

    Task<ModPackageTextureEditExecutionResult> ApplySuggestedEditAsync(
        string packagePath,
        ModPackageTextureCandidate candidate,
        CancellationToken cancellationToken = default);

    Task<ModPackageTextureEditExecutionResult> ApplyImportedTextureAsync(
        string packagePath,
        ModPackageTextureCandidate candidate,
        string sourceFilePath,
        CancellationToken cancellationToken = default);

    Task<ModPackageTextureEditExecutionResult> RollbackLatestAsync(
        string packagePath,
        string resourceKeyText,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModPackageTextureEditRecord>> GetHistoryAsync(
        string packagePath,
        string resourceKeyText,
        int maxCount = 10,
        CancellationToken cancellationToken = default);
}
