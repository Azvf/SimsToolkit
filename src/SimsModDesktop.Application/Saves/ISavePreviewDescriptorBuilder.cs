namespace SimsModDesktop.Application.Saves;

public interface ISavePreviewDescriptorBuilder
{
    Task<SavePreviewDescriptorBuildResult> BuildAsync(
        string saveFilePath,
        IProgress<SavePreviewDescriptorBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
