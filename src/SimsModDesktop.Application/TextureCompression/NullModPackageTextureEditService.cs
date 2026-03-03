namespace SimsModDesktop.Application.TextureCompression;

public sealed class NullModPackageTextureEditService : IModPackageTextureEditService
{
    public static NullModPackageTextureEditService Instance { get; } = new();

    private NullModPackageTextureEditService()
    {
    }

    public Task<ModPackageTexturePreviewResult> PreviewAsync(
        string packagePath,
        ModPackageTextureCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ModPackageTexturePreviewResult
        {
            Success = false,
            Error = "Texture edit service is unavailable."
        });
    }

    public Task<ModPackageTextureEditExecutionResult> ApplySuggestedEditAsync(
        string packagePath,
        ModPackageTextureCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ModPackageTextureEditExecutionResult
        {
            Success = false,
            ResourceKeyText = candidate.ResourceKeyText,
            Error = "Texture edit service is unavailable.",
            StatusText = "Texture edit service is unavailable."
        });
    }

    public Task<ModPackageTextureEditExecutionResult> ApplyImportedTextureAsync(
        string packagePath,
        ModPackageTextureCandidate candidate,
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ModPackageTextureEditExecutionResult
        {
            Success = false,
            ResourceKeyText = candidate.ResourceKeyText,
            Error = "Texture edit service is unavailable.",
            StatusText = "Texture edit service is unavailable."
        });
    }

    public Task<ModPackageTextureEditExecutionResult> RollbackLatestAsync(
        string packagePath,
        string resourceKeyText,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ModPackageTextureEditExecutionResult
        {
            Success = false,
            ResourceKeyText = resourceKeyText,
            Error = "Texture edit service is unavailable.",
            StatusText = "Texture edit service is unavailable."
        });
    }

    public Task<IReadOnlyList<ModPackageTextureEditRecord>> GetHistoryAsync(
        string packagePath,
        string resourceKeyText,
        int maxCount = 10,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ModPackageTextureEditRecord>>(Array.Empty<ModPackageTextureEditRecord>());
    }
}
