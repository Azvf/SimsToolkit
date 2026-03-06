using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Saves;

namespace SimsModDesktop.Application.Warmup;

public interface ISaveWarmupService
{
    Task<SavePreviewDescriptorBuildResult> EnsureDescriptorReadyAsync(
        string saveFilePath,
        CacheWarmupObserver? observer = null,
        CancellationToken cancellationToken = default);

    Task<string?> EnsureArtifactReadyAsync(
        string saveFilePath,
        string householdKey,
        string purpose,
        CancellationToken cancellationToken = default);

    bool QueueDescriptorIdlePrewarm(string saveFilePath, string trigger);

    bool QueueArtifactIdlePrewarm(string saveFilePath, string householdKey, string trigger);

    void CancelWarmup(string saveFilePath, string reason);

    bool TryGetDescriptorWarmupState(string saveFilePath, out WarmupStateSnapshot? state);

    bool TryGetArtifactWarmupState(string saveFilePath, string householdKey, out WarmupStateSnapshot? state);

    void Reset();
}
