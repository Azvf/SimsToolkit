namespace SimsModDesktop.Application.Saves;

public interface ISavePreviewCacheBuilder
{
    Task<SavePreviewCacheBuildResult> BuildAsync(
        string saveFilePath,
        IProgress<SavePreviewCacheBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
