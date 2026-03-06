using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Warmup;
using SimsModDesktop.Presentation.ViewModels;

namespace SimsModDesktop.Presentation.Warmup;

public sealed class SaveWarmupService : ISaveWarmupService
{
    private const string SavePreviewDescriptorPrimeJobType = "SavePreviewDescriptorPrime";
    private const string SavePreviewArtifactPrimeJobType = "SavePreviewArtifactPrime";
    private const string SourceKeyScopeSeparator = "\u001F";
    private readonly MainWindowCacheWarmupController _controller;
    private readonly ILogger<SaveWarmupService> _logger;
    private readonly ConcurrentDictionary<string, WarmupStateSnapshot> _descriptorWarmupStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupStateSnapshot> _artifactWarmupStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _queuedSavePaths = new(StringComparer.OrdinalIgnoreCase);

    public SaveWarmupService(
        MainWindowCacheWarmupController controller,
        ILogger<SaveWarmupService>? logger = null)
    {
        _controller = controller;
        _logger = logger ?? NullLogger<SaveWarmupService>.Instance;
    }

    public Task<SavePreviewDescriptorBuildResult> EnsureDescriptorReadyAsync(
        string saveFilePath,
        CacheWarmupObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        return EnsureDescriptorReadyCoreAsync(
            saveFilePath,
            MainWindowCacheWarmupController.CreateHost(observer),
            cancellationToken);
    }

    public Task<string?> EnsureArtifactReadyAsync(
        string saveFilePath,
        string householdKey,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        return EnsureArtifactReadyCoreAsync(saveFilePath, householdKey, purpose, cancellationToken);
    }

    public bool QueueDescriptorIdlePrewarm(string saveFilePath, string trigger)
    {
        if (_controller.BackgroundCachePrewarmCoordinator is null ||
            _controller.SaveHouseholdCoordinator is null ||
            string.IsNullOrWhiteSpace(saveFilePath))
        {
            return false;
        }

        var normalizedSavePath = _controller.ResolveFilePath(saveFilePath);
        if (string.IsNullOrWhiteSpace(normalizedSavePath) || !File.Exists(normalizedSavePath))
        {
            return false;
        }

        var queued = _controller.BackgroundCachePrewarmCoordinator.TryQueue(
            BuildSaveDescriptorPrewarmJobKey(normalizedSavePath),
            cancellationToken => EnsureDescriptorReadyCoreAsync(
                normalizedSavePath,
                _controller.CreateDetachedWarmupHost("savepreview.descriptor", trigger),
                cancellationToken),
            $"Save preview descriptor prewarm for {normalizedSavePath}");
        if (queued)
        {
            _queuedSavePaths[normalizedSavePath] = 0;
            _logger.LogInformation(
                "savepreview.descriptor.prewarm.queue savePath={SavePath} trigger={Trigger}",
                normalizedSavePath,
                trigger);
        }

        return queued;
    }

    public bool QueueArtifactIdlePrewarm(string saveFilePath, string householdKey, string trigger)
    {
        if (_controller.BackgroundCachePrewarmCoordinator is null ||
            _controller.SaveHouseholdCoordinator is null ||
            string.IsNullOrWhiteSpace(saveFilePath) ||
            string.IsNullOrWhiteSpace(householdKey))
        {
            return false;
        }

        var normalizedSavePath = _controller.ResolveFilePath(saveFilePath);
        var normalizedHouseholdKey = householdKey.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSavePath) || !File.Exists(normalizedSavePath))
        {
            return false;
        }

        var queued = _controller.BackgroundCachePrewarmCoordinator.TryQueue(
            BuildSaveArtifactPrewarmJobKey(normalizedSavePath, normalizedHouseholdKey),
            cancellationToken => EnsureArtifactReadyCoreAsync(
                normalizedSavePath,
                normalizedHouseholdKey,
                trigger,
                cancellationToken),
            $"Save preview artifact prewarm for {normalizedSavePath}:{normalizedHouseholdKey}");
        if (queued)
        {
            _queuedSavePaths[normalizedSavePath] = 0;
            _logger.LogInformation(
                "savepreview.artifact.prewarm.queue savePath={SavePath} householdKey={HouseholdKey} trigger={Trigger}",
                normalizedSavePath,
                normalizedHouseholdKey,
                trigger);
        }

        return queued;
    }

    public void CancelWarmup(string saveFilePath, string reason)
    {
        if (string.IsNullOrWhiteSpace(saveFilePath))
        {
            return;
        }

        CancelSaveSessions(_controller.ResolveFilePath(saveFilePath), reason);
    }

    public bool TryGetDescriptorWarmupState(string saveFilePath, out WarmupStateSnapshot? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(saveFilePath))
        {
            return false;
        }

        return _descriptorWarmupStates.TryGetValue(_controller.ResolveFilePath(saveFilePath), out state);
    }

    public bool TryGetArtifactWarmupState(string saveFilePath, string householdKey, out WarmupStateSnapshot? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(saveFilePath) || string.IsNullOrWhiteSpace(householdKey))
        {
            return false;
        }

        return _artifactWarmupStates.TryGetValue(
            BuildSaveArtifactSessionSource(_controller.ResolveFilePath(saveFilePath), householdKey.Trim()),
            out state);
    }

    public void Reset()
    {
        foreach (var savePath in EnumerateKnownSavePaths())
        {
            CancelSaveSessions(savePath, "reset");
        }

        _descriptorWarmupStates.Clear();
        _artifactWarmupStates.Clear();
        _queuedSavePaths.Clear();
    }

    private async Task<SavePreviewDescriptorBuildResult> EnsureDescriptorReadyCoreAsync(
        string saveFilePath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(_controller.SaveHouseholdCoordinator);

        var normalizedSavePath = _controller.ResolveFilePath(saveFilePath);
        if (_controller.SaveHouseholdCoordinator.TryGetPreviewDescriptor(normalizedSavePath, out var currentManifest) &&
            _controller.SaveHouseholdCoordinator.IsPreviewDescriptorCurrent(normalizedSavePath, currentManifest))
        {
            var readyProgress = BuildDescriptorReadyProgress(currentManifest);
            host.ReportProgress(readyProgress);
            _descriptorWarmupStates[normalizedSavePath] = new WarmupStateSnapshot
            {
                State = WarmupRunState.Completed,
                Progress = readyProgress,
                Message = "Warmup completed."
            };

            return new SavePreviewDescriptorBuildResult
            {
                Succeeded = true,
                Manifest = currentManifest
            };
        }

        var versionToken = BuildSaveVersionToken(normalizedSavePath);
        var sessionKey = WarmupSessionKey.ForSaveDescriptor(normalizedSavePath, versionToken);
        var rootGate = _controller.GetRootGate(normalizedSavePath);
        WarmupTaskSession<SavePreviewDescriptorBuildResult> session;
        await rootGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_controller.SessionRegistry.TryGet<SavePreviewDescriptorBuildResult>(sessionKey, out var existingSession) &&
                existingSession is not null &&
                existingSession.State == WarmupRunState.Running &&
                !existingSession.Task.IsCompleted)
            {
                session = existingSession;
            }
            else
            {
                session = CreateDescriptorWarmupTaskSession(normalizedSavePath, versionToken);
                _controller.SessionRegistry.Set(sessionKey, session);
            }
        }
        finally
        {
            rootGate.Release();
        }

        var hostHandle = session.AttachHost(host);
        try
        {
            return await session.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            session.DetachHost(hostHandle);
        }
    }

    private async Task<string?> EnsureArtifactReadyCoreAsync(
        string saveFilePath,
        string householdKey,
        string purpose,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(householdKey);
        ArgumentNullException.ThrowIfNull(_controller.SaveHouseholdCoordinator);

        var normalizedSavePath = _controller.ResolveFilePath(saveFilePath);
        var normalizedHouseholdKey = householdKey.Trim();
        var versionToken = BuildSaveVersionToken(normalizedSavePath);
        var sessionKey = WarmupSessionKey.ForSaveArtifact(normalizedSavePath, normalizedHouseholdKey, versionToken);
        var rootGate = _controller.GetRootGate(BuildSaveArtifactSessionSource(normalizedSavePath, normalizedHouseholdKey));
        WarmupTaskSession<string?> session;
        await rootGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_controller.SessionRegistry.TryGet<string?>(sessionKey, out var existingSession) &&
                existingSession is not null &&
                existingSession.State == WarmupRunState.Running &&
                !existingSession.Task.IsCompleted)
            {
                session = existingSession;
            }
            else
            {
                session = CreateArtifactWarmupTaskSession(normalizedSavePath, normalizedHouseholdKey, purpose, versionToken);
                _controller.SessionRegistry.Set(sessionKey, session);
            }
        }
        finally
        {
            rootGate.Release();
        }

        return await session.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private WarmupTaskSession<SavePreviewDescriptorBuildResult> CreateDescriptorWarmupTaskSession(
        string normalizedSavePath,
        string versionToken)
    {
        var sessionKey = WarmupSessionKey.ForSaveDescriptor(normalizedSavePath, versionToken);
        var session = new WarmupTaskSession<SavePreviewDescriptorBuildResult>
        {
            WarmupKey = sessionKey.ToString(),
            ModsRoot = normalizedSavePath,
            InventoryVersion = 0,
            Domain = CacheWarmupDomain.SavePreviewDescriptor,
            WorkerCts = new CancellationTokenSource(),
            Task = Task.FromResult(new SavePreviewDescriptorBuildResult())
        };
        session.MarkRunning("Warmup running.");
        _descriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
        session.Task = RunDescriptorWarmupSessionAsync(normalizedSavePath, session);
        _ = session.Task.ContinueWith(
            _ =>
            {
                session.WorkerCts.Dispose();
                _descriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return session;
    }

    private WarmupTaskSession<string?> CreateArtifactWarmupTaskSession(
        string normalizedSavePath,
        string householdKey,
        string purpose,
        string versionToken)
    {
        var sessionSource = BuildSaveArtifactSessionSource(normalizedSavePath, householdKey);
        var sessionKey = WarmupSessionKey.ForSaveArtifact(normalizedSavePath, householdKey, versionToken);
        var session = new WarmupTaskSession<string?>
        {
            WarmupKey = sessionKey.ToString(),
            ModsRoot = sessionSource,
            InventoryVersion = 0,
            Domain = CacheWarmupDomain.SavePreviewArtifact,
            WorkerCts = new CancellationTokenSource(),
            Task = Task.FromResult<string?>(null)
        };
        session.MarkRunning("Warmup running.");
        _artifactWarmupStates[sessionSource] = session.ToStateSnapshot();
        session.Task = RunArtifactWarmupSessionAsync(normalizedSavePath, householdKey, purpose, session);
        _ = session.Task.ContinueWith(
            _ =>
            {
                session.WorkerCts.Dispose();
                _artifactWarmupStates[sessionSource] = session.ToStateSnapshot();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return session;
    }

    private async Task<SavePreviewDescriptorBuildResult> RunDescriptorWarmupSessionAsync(
        string normalizedSavePath,
        WarmupTaskSession<SavePreviewDescriptorBuildResult> session)
    {
        try
        {
            var result = await _controller.SaveHouseholdCoordinator!
                .BuildPreviewDescriptorAsync(
                    normalizedSavePath,
                    new Progress<SavePreviewDescriptorBuildProgress>(progress =>
                    {
                        session.PublishProgress(new CacheWarmupProgress
                        {
                            Domain = CacheWarmupDomain.SavePreviewDescriptor,
                            Stage = "build",
                            Percent = progress.Percent,
                            Current = progress.Percent,
                            Total = 100,
                            Detail = string.IsNullOrWhiteSpace(progress.Detail)
                                ? "Preparing save preview descriptor..."
                                : progress.Detail,
                            IsBlocking = true
                        });
                        _descriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
                    }),
                    session.WorkerCts.Token)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error ?? "Failed to build save preview descriptor.");
            }

            var manifest = result.Manifest ?? new SavePreviewDescriptorManifest();
            var readyProgress = BuildDescriptorReadyProgress(manifest);
            session.PublishProgress(readyProgress);
            session.MarkCompleted(readyProgress, "Warmup completed.");
            _descriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
            return result;
        }
        catch (OperationCanceledException) when (session.WorkerCts.IsCancellationRequested)
        {
            session.MarkPaused("Warmup paused.");
            session.PublishProgress(MainWindowCacheWarmupController.BuildPausedProgress(
                CacheWarmupDomain.SavePreviewDescriptor,
                session.LastProgress,
                "Save preview descriptor warmup paused."));
            _descriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
            throw;
        }
        catch (Exception ex)
        {
            session.MarkFailed(ex, "Warmup failed.");
            _descriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
            throw;
        }
    }

    private async Task<string?> RunArtifactWarmupSessionAsync(
        string normalizedSavePath,
        string householdKey,
        string purpose,
        WarmupTaskSession<string?> session)
    {
        var sessionSource = BuildSaveArtifactSessionSource(normalizedSavePath, householdKey);
        try
        {
            session.PublishProgress(new CacheWarmupProgress
            {
                Domain = CacheWarmupDomain.SavePreviewArtifact,
                Stage = "prepare",
                Percent = 0,
                Current = 0,
                Total = 1,
                Detail = "Preparing save preview artifact...",
                IsBlocking = false
            });
            _artifactWarmupStates[sessionSource] = session.ToStateSnapshot();
            _ = await EnsureDescriptorReadyCoreAsync(
                normalizedSavePath,
                _controller.CreateDetachedWarmupHost("savepreview.descriptor", purpose),
                session.WorkerCts.Token).ConfigureAwait(false);
            var artifactRoot = await _controller.SaveHouseholdCoordinator!
                .EnsurePreviewArtifactAsync(normalizedSavePath, householdKey, purpose, session.WorkerCts.Token)
                .ConfigureAwait(false);
            var readyProgress = new CacheWarmupProgress
            {
                Domain = CacheWarmupDomain.SavePreviewArtifact,
                Stage = "ready",
                Percent = 100,
                Current = string.IsNullOrWhiteSpace(artifactRoot) ? 0 : 1,
                Total = 1,
                Detail = string.IsNullOrWhiteSpace(artifactRoot)
                    ? "Save preview artifact is unavailable."
                    : "Save preview artifact is ready.",
                IsBlocking = false
            };
            session.PublishProgress(readyProgress);
            session.MarkCompleted(readyProgress, "Warmup completed.");
            _artifactWarmupStates[sessionSource] = session.ToStateSnapshot();
            return artifactRoot;
        }
        catch (OperationCanceledException) when (session.WorkerCts.IsCancellationRequested)
        {
            session.MarkPaused("Warmup paused.");
            session.PublishProgress(MainWindowCacheWarmupController.BuildPausedProgress(
                CacheWarmupDomain.SavePreviewArtifact,
                session.LastProgress,
                "Save preview artifact warmup paused."));
            _artifactWarmupStates[sessionSource] = session.ToStateSnapshot();
            throw;
        }
        catch (Exception ex)
        {
            session.MarkFailed(ex, "Warmup failed.");
            _artifactWarmupStates[sessionSource] = session.ToStateSnapshot();
            throw;
        }
    }

    private void CancelSaveSessions(string normalizedSavePath, string reason)
    {
        _controller.BackgroundCachePrewarmCoordinator?.CancelBySource(
            normalizedSavePath,
            reason,
            SavePreviewDescriptorPrimeJobType);
        _controller.BackgroundCachePrewarmCoordinator?.CancelBySource(
            normalizedSavePath,
            reason,
            SavePreviewArtifactPrimeJobType);

        foreach (var entry in _controller.SessionRegistry.FindByDomainAndSource<SavePreviewDescriptorBuildResult>(
                     normalizedSavePath,
                     CacheWarmupDomain.SavePreviewDescriptor).ToArray())
        {
            MainWindowCacheWarmupController.SafeCancelToken(entry.Value.WorkerCts);
            _controller.SessionRegistry.TryRemove(entry.Key, out WarmupTaskSession<SavePreviewDescriptorBuildResult>? _);
        }

        foreach (var entry in _controller.SessionRegistry.FindBySourcePrefix<string?>(
                     normalizedSavePath + SourceKeyScopeSeparator,
                     CacheWarmupDomain.SavePreviewArtifact).ToArray())
        {
            MainWindowCacheWarmupController.SafeCancelToken(entry.Value.WorkerCts);
            _controller.SessionRegistry.TryRemove(entry.Key, out WarmupTaskSession<string?>? _);
        }

        _descriptorWarmupStates.TryRemove(normalizedSavePath, out _);
        _queuedSavePaths.TryRemove(normalizedSavePath, out _);
        foreach (var key in _artifactWarmupStates.Keys.Where(key =>
                     key.StartsWith(normalizedSavePath + SourceKeyScopeSeparator, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _artifactWarmupStates.TryRemove(key, out _);
        }
    }

    private static BackgroundPrewarmJobKey BuildSaveDescriptorPrewarmJobKey(string normalizedSavePath)
    {
        return new BackgroundPrewarmJobKey
        {
            JobType = SavePreviewDescriptorPrimeJobType,
            SourceKey = normalizedSavePath
        };
    }

    private static BackgroundPrewarmJobKey BuildSaveArtifactPrewarmJobKey(string normalizedSavePath, string householdKey)
    {
        return new BackgroundPrewarmJobKey
        {
            JobType = SavePreviewArtifactPrimeJobType,
            SourceKey = BuildSaveArtifactSessionSource(normalizedSavePath, householdKey)
        };
    }

    private static string BuildSaveArtifactSessionSource(string normalizedSavePath, string householdKey)
    {
        return normalizedSavePath + SourceKeyScopeSeparator + householdKey;
    }

    private static string BuildSaveVersionToken(string normalizedSavePath)
    {
        var file = new FileInfo(normalizedSavePath);
        if (!file.Exists)
        {
            return "missing";
        }

        return string.Join(
            ":",
            file.Length.ToString(CultureInfo.InvariantCulture),
            file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    private static CacheWarmupProgress BuildDescriptorReadyProgress(SavePreviewDescriptorManifest manifest)
    {
        return new CacheWarmupProgress
        {
            Domain = CacheWarmupDomain.SavePreviewDescriptor,
            Stage = "ready",
            Percent = 100,
            Current = manifest.ReadyHouseholdCount,
            Total = manifest.TotalHouseholdCount,
            Detail = "Save preview descriptor is ready.",
            IsBlocking = true
        };
    }

    private IEnumerable<string> EnumerateKnownSavePaths()
    {
        return _descriptorWarmupStates.Keys
            .Concat(_queuedSavePaths.Keys)
            .Concat(_artifactWarmupStates.Keys.Select(ParseSavePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ParseSavePath(string sessionSource)
    {
        var separatorIndex = sessionSource.IndexOf(SourceKeyScopeSeparator, StringComparison.Ordinal);
        return separatorIndex >= 0 ? sessionSource[..separatorIndex] : sessionSource;
    }
}
