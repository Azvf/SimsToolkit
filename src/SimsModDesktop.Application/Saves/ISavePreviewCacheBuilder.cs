namespace SimsModDesktop.Application.Saves;

public interface ISavePreviewCacheBuilder
{
    Task<SavePreviewCacheBuildResult> BuildAsync(
        string saveFilePath,
        IProgress<SavePreviewCacheBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<SavePreviewCacheBuildResult> BuildAsync(
        string saveFilePath,
        SavePreviewBuildOptions? options,
        IProgress<SavePreviewCacheBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return BuildAsync(saveFilePath, progress, cancellationToken);
    }
}

public sealed record SavePreviewBuildOptions
{
    public int? WorkerCount { get; init; }
    public bool ContinueOnItemFailure { get; init; } = true;
}
