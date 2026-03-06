using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.PackageCore;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowCacheWarmupController
{
    private const string TrayDependencySnapshotPrewarmJobType = "TrayDependencySnapshotPrewarm";
    private const string ModCatalogQueryPrimeJobType = "ModCatalogQueryPrime";
    private const string SavePreviewDescriptorPrimeJobType = "SavePreviewDescriptorPrime";
    private const string SavePreviewArtifactPrimeJobType = "SavePreviewArtifactPrime";
    private const string SourceKeyScopeSeparator = "\u001F";
    private readonly IModPackageInventoryService _inventoryService;
    private readonly IModItemIndexScheduler _indexScheduler;
    private readonly IModItemCatalogService? _modItemCatalogService;
    private readonly ISaveHouseholdCoordinator? _saveHouseholdCoordinator;
    private readonly IPackageIndexCache _packageIndexCache;
    private readonly ILogger<MainWindowCacheWarmupController> _logger;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private readonly IConfigurationProvider? _configurationProvider;
    private readonly IBackgroundCachePrewarmCoordinator? _backgroundCachePrewarmCoordinator;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _rootGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ModPackageInventoryRefreshResult> _inventoryResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, InventoryRefreshTaskSession> _inventoryRefreshTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _modsReadyInventoryVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PackageIndexSnapshot> _trayReadySnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupTaskSession<ModPackageInventoryRefreshResult>> _modsWarmupTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupTaskSession<PackageIndexSnapshot>> _trayWarmupTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupTaskSession<SavePreviewDescriptorBuildResult>> _saveDescriptorWarmupTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupTaskSession<string?>> _saveArtifactWarmupTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _rootWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupStateSnapshot> _modsWarmupStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupStateSnapshot> _trayWarmupStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupStateSnapshot> _saveDescriptorWarmupStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupStateSnapshot> _saveArtifactWarmupStates = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowCacheWarmupController(
        IModPackageInventoryService inventoryService,
        IModItemIndexScheduler indexScheduler,
        IPackageIndexCache packageIndexCache,
        ILogger<MainWindowCacheWarmupController> logger,
        IModItemCatalogService? modItemCatalogService = null,
        ISaveHouseholdCoordinator? saveHouseholdCoordinator = null,
        IPathIdentityResolver? pathIdentityResolver = null,
        IConfigurationProvider? configurationProvider = null,
        IBackgroundCachePrewarmCoordinator? backgroundCachePrewarmCoordinator = null)
    {
        _inventoryService = inventoryService;
        _indexScheduler = indexScheduler;
        _modItemCatalogService = modItemCatalogService;
        _saveHouseholdCoordinator = saveHouseholdCoordinator;
        _packageIndexCache = packageIndexCache;
        _logger = logger;
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
        _configurationProvider = configurationProvider;
        _backgroundCachePrewarmCoordinator = backgroundCachePrewarmCoordinator;
    }

    internal enum WarmupRunState
    {
        Idle,
        Running,
        Paused,
        Completed,
        Failed
    }

    internal sealed record WarmupStateSnapshot
    {
        public WarmupRunState State { get; init; }
        public CacheWarmupProgress Progress { get; init; } = new();
        public long InventoryVersion { get; init; }
        public string Message { get; init; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    }

    private sealed class InventoryRefreshTaskSession
    {
        public required string ModsRoot { get; init; }
        public required CancellationTokenSource WorkerCts { get; init; }
        public required Task<ModPackageInventoryRefreshResult> Task { get; init; }
    }

    private sealed class WarmupTaskSession<T>
    {
        private readonly ConcurrentDictionary<int, MainWindowCacheWarmupHost> _hosts = new();
        private int _hostSequence;
        private readonly object _stateSync = new();

        public required string WarmupKey { get; init; }
        public required string ModsRoot { get; init; }
        public required long InventoryVersion { get; init; }
        public required CacheWarmupDomain Domain { get; init; }
        public required CancellationTokenSource WorkerCts { get; init; }
        public required Task<T> Task { get; set; }
        public WarmupRunState State { get; private set; } = WarmupRunState.Running;
        public CacheWarmupProgress LastProgress { get; private set; } = new();
        public string LastMessage { get; private set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
        public Exception? LastError { get; private set; }

        public int AttachHost(MainWindowCacheWarmupHost host)
        {
            var handle = Interlocked.Increment(ref _hostSequence);
            _hosts[handle] = host;
            var progress = LastProgress;
            if (!string.IsNullOrWhiteSpace(progress.Stage) || !string.IsNullOrWhiteSpace(progress.Detail))
            {
                SafeReportProgress(host, progress);
            }

            return handle;
        }

        public void DetachHost(int handle)
        {
            _hosts.TryRemove(handle, out _);
        }

        public void PublishProgress(CacheWarmupProgress progress)
        {
            lock (_stateSync)
            {
                LastProgress = progress;
                UpdatedAtUtc = DateTimeOffset.UtcNow;
                if (State == WarmupRunState.Running && progress.Percent >= 100)
                {
                    State = WarmupRunState.Completed;
                }
            }

            foreach (var host in _hosts.Values)
            {
                SafeReportProgress(host, progress);
            }
        }

        public void PublishLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (_stateSync)
            {
                LastMessage = message;
                UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            foreach (var host in _hosts.Values)
            {
                SafeAppendLog(host, message);
            }
        }

        public void MarkRunning(string message)
        {
            lock (_stateSync)
            {
                State = WarmupRunState.Running;
                LastMessage = message;
                UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        public bool RequestPause(string reason)
        {
            lock (_stateSync)
            {
                if (State != WarmupRunState.Running)
                {
                    return false;
                }

                State = WarmupRunState.Paused;
                LastMessage = reason;
                UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            MainWindowCacheWarmupController.SafeCancelToken(WorkerCts);
            return true;
        }

        public void MarkPaused(string message)
        {
            lock (_stateSync)
            {
                State = WarmupRunState.Paused;
                LastMessage = message;
                UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        public void MarkCompleted(CacheWarmupProgress readyProgress, string message)
        {
            lock (_stateSync)
            {
                State = WarmupRunState.Completed;
                LastMessage = message;
                LastProgress = readyProgress;
                UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        public void MarkFailed(Exception exception, string message)
        {
            lock (_stateSync)
            {
                State = WarmupRunState.Failed;
                LastError = exception;
                LastMessage = message;
                UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        public WarmupStateSnapshot ToStateSnapshot()
        {
            lock (_stateSync)
            {
                return new WarmupStateSnapshot
                {
                    State = State,
                    Progress = LastProgress,
                    InventoryVersion = InventoryVersion,
                    Message = LastMessage,
                    UpdatedAtUtc = UpdatedAtUtc
                };
            }
        }

        private static void SafeReportProgress(MainWindowCacheWarmupHost host, CacheWarmupProgress progress)
        {
            try
            {
                host.ReportProgress(progress);
            }
            catch
            {
            }
        }

        private static void SafeAppendLog(MainWindowCacheWarmupHost host, string message)
        {
            try
            {
                host.AppendLog(message);
            }
            catch
            {
            }
        }
    }

    internal async Task<ModPackageInventoryRefreshResult> EnsureModsWorkspaceReadyAsync(
        string modsRootPath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRootPath);
        ArgumentNullException.ThrowIfNull(host);

        var resolvedRoot = ResolveDirectory(modsRootPath);
        var normalizedRoot = resolvedRoot.CanonicalPath;
        host.AppendLog(
            $"[path.resolve] component=modcache.warmup rawPath={resolvedRoot.FullPath} canonicalPath={resolvedRoot.CanonicalPath} exists={resolvedRoot.Exists} isReparse={resolvedRoot.IsReparsePoint} linkTarget={resolvedRoot.LinkTarget ?? string.Empty}");

        var inventory = await EnsureInventoryCoreAsync(normalizedRoot, host, cancellationToken).ConfigureAwait(false);
        if (_modsReadyInventoryVersions.TryGetValue(normalizedRoot, out var readyInventoryVersion) &&
            readyInventoryVersion == inventory.Snapshot.InventoryVersion)
        {
            host.ReportProgress(new CacheWarmupProgress
            {
                Domain = CacheWarmupDomain.ModsCatalog,
                Stage = "ready",
                Percent = 100,
                Current = inventory.Snapshot.Entries.Count,
                Total = inventory.Snapshot.Entries.Count,
                Detail = "Mods catalog cache is ready.",
                IsBlocking = true
            });
            _modsWarmupStates[normalizedRoot] = new WarmupStateSnapshot
            {
                State = WarmupRunState.Completed,
                InventoryVersion = inventory.Snapshot.InventoryVersion,
                Message = "Warmup completed.",
                Progress = new CacheWarmupProgress
                {
                    Domain = CacheWarmupDomain.ModsCatalog,
                    Stage = "ready",
                    Percent = 100,
                    Current = inventory.Snapshot.Entries.Count,
                    Total = inventory.Snapshot.Entries.Count,
                    Detail = "Mods catalog cache is ready.",
                    IsBlocking = true
                }
            };
            return inventory;
        }

        var warmupKey = BuildWarmupKey(CacheWarmupDomain.ModsCatalog, normalizedRoot, inventory.Snapshot.InventoryVersion);
        var rootGate = GetRootGate(normalizedRoot);
        WarmupTaskSession<ModPackageInventoryRefreshResult> session;
        var gateWaitStarted = Stopwatch.StartNew();
        await rootGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (gateWaitStarted.ElapsedMilliseconds > 0)
        {
            _logger.LogInformation(
                "modcache.warmup.lock.wait modsRoot={ModsRoot} elapsedMs={ElapsedMs}",
                normalizedRoot,
                gateWaitStarted.ElapsedMilliseconds);
        }

        try
        {
            if (_modsWarmupTasks.TryGetValue(warmupKey, out var existingSession))
            {
                if (existingSession.State == WarmupRunState.Running && !existingSession.Task.IsCompleted)
                {
                    session = existingSession;
                    session.PublishLog($"[modcache.fastindex.session.reuse] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion}");
                    _logger.LogInformation(
                        "modcache.fastindex.session.reuse modsRoot={ModsRoot} inventoryVersion={InventoryVersion}",
                        normalizedRoot,
                        inventory.Snapshot.InventoryVersion);
                }
                else
                {
                    if (existingSession.State == WarmupRunState.Paused)
                    {
                        host.AppendLog($"[modcache.fastindex.resume] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion}");
                        _logger.LogInformation(
                            "modcache.fastindex.resume modsRoot={ModsRoot} inventoryVersion={InventoryVersion}",
                            normalizedRoot,
                            inventory.Snapshot.InventoryVersion);
                    }
                    _modsWarmupTasks.TryRemove(warmupKey, out _);
                    session = CreateModsWarmupTaskSession(normalizedRoot, inventory);
                    _modsWarmupTasks[warmupKey] = session;
                }
            }
            else
            {
                session = CreateModsWarmupTaskSession(normalizedRoot, inventory);
                _modsWarmupTasks[warmupKey] = session;
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

    internal async Task<PackageIndexSnapshot> EnsureTrayWorkspaceReadyAsync(
        string modsRootPath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRootPath);
        ArgumentNullException.ThrowIfNull(host);

        var resolvedRoot = ResolveDirectory(modsRootPath);
        var normalizedRoot = resolvedRoot.CanonicalPath;
        host.AppendLog(
            $"[path.resolve] component=traycache.warmup rawPath={resolvedRoot.FullPath} canonicalPath={resolvedRoot.CanonicalPath} exists={resolvedRoot.Exists} isReparse={resolvedRoot.IsReparsePoint} linkTarget={resolvedRoot.LinkTarget ?? string.Empty}");

        var inventory = await EnsureInventoryCoreAsync(normalizedRoot, host, cancellationToken).ConfigureAwait(false);
        if (_trayReadySnapshots.TryGetValue(normalizedRoot, out var readySnapshot) &&
            readySnapshot.InventoryVersion == inventory.Snapshot.InventoryVersion)
        {
            host.ReportProgress(new CacheWarmupProgress
            {
                Domain = CacheWarmupDomain.TrayDependency,
                Stage = "ready",
                Percent = 100,
                Current = inventory.Snapshot.Entries.Count,
                Total = inventory.Snapshot.Entries.Count,
                Detail = "Tray dependency cache is ready.",
                IsBlocking = true
            });
            _trayWarmupStates[normalizedRoot] = new WarmupStateSnapshot
            {
                State = WarmupRunState.Completed,
                InventoryVersion = inventory.Snapshot.InventoryVersion,
                Message = "Warmup completed.",
                Progress = new CacheWarmupProgress
                {
                    Domain = CacheWarmupDomain.TrayDependency,
                    Stage = "ready",
                    Percent = 100,
                    Current = readySnapshot.Packages.Count,
                    Total = readySnapshot.Packages.Count,
                    Detail = "Tray dependency cache is ready.",
                    IsBlocking = true
                }
            };
            return readySnapshot;
        }

        var warmupKey = BuildWarmupKey(CacheWarmupDomain.TrayDependency, normalizedRoot, inventory.Snapshot.InventoryVersion);
        var rootGate = GetRootGate(normalizedRoot);
        WarmupTaskSession<PackageIndexSnapshot> session;
        var gateWaitStarted = Stopwatch.StartNew();
        await rootGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (gateWaitStarted.ElapsedMilliseconds > 0)
        {
            _logger.LogInformation(
                "modcache.warmup.lock.wait modsRoot={ModsRoot} elapsedMs={ElapsedMs}",
                normalizedRoot,
                gateWaitStarted.ElapsedMilliseconds);
        }

        try
        {
            if (_trayWarmupTasks.TryGetValue(warmupKey, out var existingSession))
            {
                if (existingSession.State == WarmupRunState.Running && !existingSession.Task.IsCompleted)
                {
                    session = existingSession;
                    session.PublishLog($"[traycache.snapshot.inflight.wait] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion}");
                    _logger.LogInformation(
                        "traycache.snapshot.session.reuse modsRoot={ModsRoot} inventoryVersion={InventoryVersion}",
                        normalizedRoot,
                        inventory.Snapshot.InventoryVersion);
                }
                else
                {
                    if (existingSession.State == WarmupRunState.Paused)
                    {
                        host.AppendLog($"[traycache.snapshot.resume] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion}");
                        _logger.LogInformation(
                            "traycache.snapshot.resume modsRoot={ModsRoot} inventoryVersion={InventoryVersion}",
                            normalizedRoot,
                            inventory.Snapshot.InventoryVersion);
                    }
                    _trayWarmupTasks.TryRemove(warmupKey, out _);
                    session = CreateTrayWarmupTaskSession(normalizedRoot, inventory);
                    _trayWarmupTasks[warmupKey] = session;
                }
            }
            else
            {
                session = CreateTrayWarmupTaskSession(normalizedRoot, inventory);
                _trayWarmupTasks[warmupKey] = session;
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

    internal bool QueueTrayDependencyIdlePrewarm(string modsRootPath, string trigger)
    {
        if (_backgroundCachePrewarmCoordinator is null || string.IsNullOrWhiteSpace(modsRootPath))
        {
            return false;
        }

        var normalizedRoot = ResolveDirectoryPath(modsRootPath);
        if (string.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
        {
            return false;
        }

        var key = BuildTrayPrewarmJobKey(normalizedRoot);
        var queued = _backgroundCachePrewarmCoordinator.TryQueue(
            key,
            cancellationToken => EnsureTrayWorkspaceReadyAsync(
                normalizedRoot,
                CreateDetachedWarmupHost("traycache", trigger),
                cancellationToken),
            $"Tray dependency snapshot prewarm for {normalizedRoot}");
        if (queued)
        {
            _logger.LogInformation(
                "traycache.prewarm.queue modsRoot={ModsRoot} trigger={Trigger}",
                normalizedRoot,
                trigger);
        }

        return queued;
    }

    internal bool QueueModsQueryIdlePrewarm(ModItemCatalogQuery query, string trigger)
    {
        if (_backgroundCachePrewarmCoordinator is null ||
            _modItemCatalogService is null ||
            string.IsNullOrWhiteSpace(query.ModsRoot))
        {
            return false;
        }

        var normalizedQuery = NormalizeModCatalogQuery(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery.ModsRoot) || !Directory.Exists(normalizedQuery.ModsRoot))
        {
            return false;
        }

        var key = BuildModCatalogPrewarmJobKey(normalizedQuery);
        var queued = _backgroundCachePrewarmCoordinator.TryQueue(
            key,
            cancellationToken => PrimeModCatalogQueryAsync(normalizedQuery, trigger, cancellationToken),
            $"Mods catalog query prewarm for {normalizedQuery.ModsRoot}");
        if (queued)
        {
            _logger.LogInformation(
                "modquery.prewarm.queue modsRoot={ModsRoot} trigger={Trigger} fingerprint={Fingerprint}",
                normalizedQuery.ModsRoot,
                trigger,
                BuildModQueryFingerprint(normalizedQuery));
        }

        return queued;
    }

    internal bool TryGetModsQueryPrewarmState(
        ModItemCatalogQuery query,
        out BackgroundPrewarmJobState? state)
    {
        state = null;
        if (_backgroundCachePrewarmCoordinator is null || string.IsNullOrWhiteSpace(query.ModsRoot))
        {
            return false;
        }

        var normalizedQuery = NormalizeModCatalogQuery(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery.ModsRoot))
        {
            return false;
        }

        return _backgroundCachePrewarmCoordinator.TryGetState(
            BuildModCatalogPrewarmJobKey(normalizedQuery),
            out state);
    }

    internal bool TryGetTrayPrewarmState(
        string modsRootPath,
        out BackgroundPrewarmJobState? state)
    {
        state = null;
        if (_backgroundCachePrewarmCoordinator is null || string.IsNullOrWhiteSpace(modsRootPath))
        {
            return false;
        }

        var normalizedRoot = ResolveDirectoryPath(modsRootPath);
        return _backgroundCachePrewarmCoordinator.TryGetState(
            BuildTrayPrewarmJobKey(normalizedRoot),
            out state);
    }

    internal async Task<SavePreviewDescriptorBuildResult> EnsureSavePreviewDescriptorReadyAsync(
        string saveFilePath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(_saveHouseholdCoordinator);

        var normalizedSavePath = ResolveFilePath(saveFilePath);
        if (_saveHouseholdCoordinator.TryGetPreviewDescriptor(normalizedSavePath, out var currentManifest) &&
            _saveHouseholdCoordinator.IsPreviewDescriptorCurrent(normalizedSavePath, currentManifest))
        {
            var readyProgress = new CacheWarmupProgress
            {
                Domain = CacheWarmupDomain.SavePreviewDescriptor,
                Stage = "ready",
                Percent = 100,
                Current = currentManifest.ReadyHouseholdCount,
                Total = currentManifest.TotalHouseholdCount,
                Detail = "Save preview descriptor is ready.",
                IsBlocking = true
            };
            host.ReportProgress(readyProgress);
            _saveDescriptorWarmupStates[normalizedSavePath] = new WarmupStateSnapshot
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
        var warmupKey = BuildSaveDescriptorWarmupKey(normalizedSavePath, versionToken);
        var rootGate = GetRootGate(normalizedSavePath);
        WarmupTaskSession<SavePreviewDescriptorBuildResult> session;
        await rootGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_saveDescriptorWarmupTasks.TryGetValue(warmupKey, out var existingSession) &&
                existingSession.State == WarmupRunState.Running &&
                !existingSession.Task.IsCompleted)
            {
                session = existingSession;
            }
            else
            {
                session = CreateSaveDescriptorWarmupTaskSession(normalizedSavePath, versionToken);
                _saveDescriptorWarmupTasks[warmupKey] = session;
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

    internal async Task<SavePreviewDescriptorBuildResult?> AttachToInflightSavePreviewDescriptorWarmupIfAny(
        string saveFilePath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentNullException.ThrowIfNull(host);

        var normalizedSavePath = ResolveFilePath(saveFilePath);
        var session = _saveDescriptorWarmupTasks.Values
            .Where(candidate =>
                string.Equals(candidate.ModsRoot, normalizedSavePath, StringComparison.OrdinalIgnoreCase) &&
                candidate.State == WarmupRunState.Running &&
                !candidate.Task.IsCompleted)
            .OrderByDescending(candidate => candidate.UpdatedAtUtc)
            .FirstOrDefault();
        if (session is null)
        {
            return null;
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

    internal bool QueueSavePreviewDescriptorIdlePrewarm(string saveFilePath, string trigger)
    {
        if (_backgroundCachePrewarmCoordinator is null ||
            _saveHouseholdCoordinator is null ||
            string.IsNullOrWhiteSpace(saveFilePath))
        {
            return false;
        }

        var normalizedSavePath = ResolveFilePath(saveFilePath);
        if (string.IsNullOrWhiteSpace(normalizedSavePath) || !File.Exists(normalizedSavePath))
        {
            return false;
        }

        var queued = _backgroundCachePrewarmCoordinator.TryQueue(
            BuildSaveDescriptorPrewarmJobKey(normalizedSavePath),
            cancellationToken => EnsureSavePreviewDescriptorReadyAsync(
                normalizedSavePath,
                CreateDetachedWarmupHost("savepreview.descriptor", trigger),
                cancellationToken),
            $"Save preview descriptor prewarm for {normalizedSavePath}");
        if (queued)
        {
            _logger.LogInformation(
                "savepreview.descriptor.prewarm.queue savePath={SavePath} trigger={Trigger}",
                normalizedSavePath,
                trigger);
        }

        return queued;
    }

    internal async Task<string?> EnsureSavePreviewArtifactReadyAsync(
        string saveFilePath,
        string householdKey,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(householdKey);
        ArgumentNullException.ThrowIfNull(_saveHouseholdCoordinator);

        var normalizedSavePath = ResolveFilePath(saveFilePath);
        var normalizedHouseholdKey = householdKey.Trim();
        var versionToken = BuildSaveVersionToken(normalizedSavePath);
        var warmupKey = BuildSaveArtifactWarmupKey(normalizedSavePath, normalizedHouseholdKey, versionToken);
        var rootGate = GetRootGate(normalizedSavePath + SourceKeyScopeSeparator + normalizedHouseholdKey);
        WarmupTaskSession<string?> session;
        await rootGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_saveArtifactWarmupTasks.TryGetValue(warmupKey, out var existingSession) &&
                existingSession.State == WarmupRunState.Running &&
                !existingSession.Task.IsCompleted)
            {
                session = existingSession;
            }
            else
            {
                session = CreateSaveArtifactWarmupTaskSession(normalizedSavePath, normalizedHouseholdKey, purpose, versionToken);
                _saveArtifactWarmupTasks[warmupKey] = session;
            }
        }
        finally
        {
            rootGate.Release();
        }

        return await session.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<string?> AttachToInflightSavePreviewArtifactWarmupIfAny(
        string saveFilePath,
        string householdKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(householdKey);

        var normalizedSavePath = ResolveFilePath(saveFilePath);
        var normalizedHouseholdKey = householdKey.Trim();
        var session = _saveArtifactWarmupTasks.Values
            .Where(candidate =>
                string.Equals(candidate.ModsRoot, BuildSaveArtifactSessionSource(normalizedSavePath, normalizedHouseholdKey), StringComparison.OrdinalIgnoreCase) &&
                candidate.State == WarmupRunState.Running &&
                !candidate.Task.IsCompleted)
            .OrderByDescending(candidate => candidate.UpdatedAtUtc)
            .FirstOrDefault();
        if (session is null)
        {
            return null;
        }

        return await session.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal bool QueueSavePreviewArtifactIdlePrewarm(
        string saveFilePath,
        string householdKey,
        string trigger)
    {
        if (_backgroundCachePrewarmCoordinator is null ||
            _saveHouseholdCoordinator is null ||
            string.IsNullOrWhiteSpace(saveFilePath) ||
            string.IsNullOrWhiteSpace(householdKey))
        {
            return false;
        }

        var normalizedSavePath = ResolveFilePath(saveFilePath);
        var normalizedHouseholdKey = householdKey.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSavePath) || !File.Exists(normalizedSavePath))
        {
            return false;
        }

        var queued = _backgroundCachePrewarmCoordinator.TryQueue(
            BuildSaveArtifactPrewarmJobKey(normalizedSavePath, normalizedHouseholdKey),
            cancellationToken => EnsureSavePreviewArtifactReadyAsync(
                normalizedSavePath,
                normalizedHouseholdKey,
                trigger,
                cancellationToken),
            $"Save preview artifact prewarm for {normalizedSavePath}:{normalizedHouseholdKey}");
        if (queued)
        {
            _logger.LogInformation(
                "savepreview.artifact.prewarm.queue savePath={SavePath} householdKey={HouseholdKey} trigger={Trigger}",
                normalizedSavePath,
                normalizedHouseholdKey,
                trigger);
        }

        return queued;
    }

    internal bool TryGetSavePreviewDescriptorPrewarmState(
        string saveFilePath,
        out BackgroundPrewarmJobState? state)
    {
        state = null;
        if (_backgroundCachePrewarmCoordinator is null || string.IsNullOrWhiteSpace(saveFilePath))
        {
            return false;
        }

        return _backgroundCachePrewarmCoordinator.TryGetState(
            BuildSaveDescriptorPrewarmJobKey(ResolveFilePath(saveFilePath)),
            out state);
    }

    internal bool TryGetSavePreviewArtifactPrewarmState(
        string saveFilePath,
        string householdKey,
        out BackgroundPrewarmJobState? state)
    {
        state = null;
        if (_backgroundCachePrewarmCoordinator is null ||
            string.IsNullOrWhiteSpace(saveFilePath) ||
            string.IsNullOrWhiteSpace(householdKey))
        {
            return false;
        }

        return _backgroundCachePrewarmCoordinator.TryGetState(
            BuildSaveArtifactPrewarmJobKey(ResolveFilePath(saveFilePath), householdKey.Trim()),
            out state);
    }

    internal void CancelSavePreviewWarmup(string saveFilePath, string reason)
    {
        if (string.IsNullOrWhiteSpace(saveFilePath))
        {
            return;
        }

        var normalizedSavePath = ResolveFilePath(saveFilePath);
        CancelSaveSessions(normalizedSavePath, reason);
    }

    internal async Task<PackageIndexSnapshot?> AttachToInflightTrayWarmupIfAny(
        string modsRootPath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRootPath);
        ArgumentNullException.ThrowIfNull(host);

        var normalizedRoot = ResolveDirectoryPath(modsRootPath);
        var session = _trayWarmupTasks.Values
            .Where(candidate =>
                string.Equals(candidate.ModsRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                candidate.State == WarmupRunState.Running &&
                !candidate.Task.IsCompleted)
            .OrderByDescending(candidate => candidate.InventoryVersion)
            .FirstOrDefault();
        if (session is null)
        {
            return null;
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

    internal void PauseModsWarmup(string modsRootPath, string reason)
    {
        PauseWarmup(CacheWarmupDomain.ModsCatalog, modsRootPath, reason);
    }

    internal void PauseTrayWarmup(string modsRootPath, string reason)
    {
        PauseWarmup(CacheWarmupDomain.TrayDependency, modsRootPath, reason);
    }

    internal bool TryGetWarmupState(
        string modsRootPath,
        CacheWarmupDomain domain,
        out WarmupStateSnapshot? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(modsRootPath))
        {
            return false;
        }

        var normalizedRoot = ResolveDirectoryPath(modsRootPath);
        var source = domain == CacheWarmupDomain.ModsCatalog
            ? _modsWarmupStates
            : _trayWarmupStates;
        return source.TryGetValue(normalizedRoot, out state);
    }

    private void PauseWarmup(CacheWarmupDomain domain, string modsRootPath, string reason)
    {
        if (string.IsNullOrWhiteSpace(modsRootPath))
        {
            return;
        }

        var normalizedRoot = ResolveDirectoryPath(modsRootPath);
        if (_inventoryRefreshTasks.TryGetValue(normalizedRoot, out var inventorySession))
        {
            SafeCancelToken(inventorySession.WorkerCts);
            _logger.LogInformation(
                "modcache.inventory.pause modsRoot={ModsRoot} reason={Reason}",
                normalizedRoot,
                reason);
        }

        var message = string.IsNullOrWhiteSpace(reason) ? "paused" : reason;
        if (domain == CacheWarmupDomain.ModsCatalog)
        {
            foreach (var entry in _modsWarmupTasks.Values.Where(session =>
                         string.Equals(session.ModsRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase)))
            {
                if (!entry.RequestPause(message))
                {
                    continue;
                }

                entry.PublishLog($"[modcache.fastindex.pause] modsRoot={normalizedRoot} inventoryVersion={entry.InventoryVersion} reason={message}");
                _logger.LogInformation(
                    "modcache.fastindex.pause modsRoot={ModsRoot} inventoryVersion={InventoryVersion} reason={Reason}",
                    normalizedRoot,
                    entry.InventoryVersion,
                    message);
                _modsWarmupStates[normalizedRoot] = entry.ToStateSnapshot();
            }

            return;
        }

        foreach (var entry in _trayWarmupTasks.Values.Where(session =>
                     string.Equals(session.ModsRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase)))
        {
            if (!entry.RequestPause(message))
            {
                continue;
            }

            entry.PublishLog($"[traycache.snapshot.pause] modsRoot={normalizedRoot} inventoryVersion={entry.InventoryVersion} reason={message}");
            _logger.LogInformation(
                "traycache.snapshot.pause modsRoot={ModsRoot} inventoryVersion={InventoryVersion} reason={Reason}",
                normalizedRoot,
                entry.InventoryVersion,
                message);
            _trayWarmupStates[normalizedRoot] = entry.ToStateSnapshot();
        }
    }

    private async Task<PackageIndexSnapshot> LoadOrBuildTraySnapshotAsync(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory,
        WarmupTaskSession<PackageIndexSnapshot> session,
        CancellationToken cancellationToken)
    {
        PackageIndexSnapshot? snapshot;
        using var timing = PerformanceLogScope.Begin(
            _logger,
            "traycache.snapshot",
            ("modsRoot", normalizedRoot),
            ("inventoryVersion", inventory.Snapshot.InventoryVersion),
            ("packageCount", inventory.Snapshot.Entries.Count));

        using var loadTiming = PerformanceLogScope.Begin(
            _logger,
            "traycache.snapshot.load",
            ("modsRoot", normalizedRoot),
            ("inventoryVersion", inventory.Snapshot.InventoryVersion));
        try
        {
            snapshot = await _packageIndexCache.TryLoadSnapshotAsync(
                normalizedRoot,
                inventory.Snapshot.InventoryVersion,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            loadTiming.Cancel("load-cancelled");
            throw;
        }
        catch (Exception ex)
        {
            loadTiming.Fail(ex, "load-failed");
            throw;
        }

        if (snapshot is not null)
        {
            session.PublishLog(
                $"[traycache.snapshot.load-hit] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
            loadTiming.Success(
                "load-hit",
                ("packageCount", snapshot.Packages.Count));
            timing.Mark(
                "snapshot.load-hit",
                ("packageCount", snapshot.Packages.Count));
            timing.Success(
                "ready",
                ("inventoryVersion", inventory.Snapshot.InventoryVersion),
                ("packageCount", snapshot.Packages.Count));
            return snapshot;
        }

        loadTiming.Skip("load-miss");
        session.PublishLog(
            $"[traycache.snapshot.build.start] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={inventory.Snapshot.Entries.Count}");
        using var buildTiming = PerformanceLogScope.Begin(
            _logger,
            "traycache.snapshot.build",
            ("modsRoot", normalizedRoot),
            ("inventoryVersion", inventory.Snapshot.InventoryVersion),
            ("packageCount", inventory.Snapshot.Entries.Count));
        var stageStopwatch = new Stopwatch();
        var currentStageLabel = string.Empty;
        var currentStagePercent = 0;
        var useRound2TrayCachePipeline = await GetRound2ConfigBoolAsync(
            "Performance.Round2.TrayCachePipelineEnabled",
            defaultValue: true,
            cancellationToken).ConfigureAwait(false);
        var parseWorkers = await GetConfigIntAsync(
            "Performance.TrayCache.ParseWorkers",
            defaultValue: null,
            cancellationToken).ConfigureAwait(false);
        var writeBatchSize = await GetConfigIntAsync(
            "Performance.TrayCache.WriteBatchSize",
            defaultValue: null,
            cancellationToken).ConfigureAwait(false);
        var parseResultChannelCapacity = await GetConfigIntAsync(
            "Performance.TrayCache.ParseResultChannelCapacity",
            defaultValue: null,
            cancellationToken).ConfigureAwait(false);
        var commitBatchSize = await GetConfigIntAsync(
            "Performance.TrayCache.CommitBatchSize",
            defaultValue: null,
            cancellationToken).ConfigureAwait(false);
        var commitIntervalMs = await GetConfigIntAsync(
            "Performance.TrayCache.CommitIntervalMs",
            defaultValue: null,
            cancellationToken).ConfigureAwait(false);
        var useAdaptiveThrottleV2 = await GetRound2ConfigBoolAsync(
            "Performance.Round2.TrayCacheThrottleV2Enabled",
            defaultValue: true,
            cancellationToken).ConfigureAwait(false);
        var useIncrementalOrphanCleanup = await GetRound2ConfigBoolAsync(
            "Performance.TrayCache.IncrementalOrphanCleanup",
            defaultValue: true,
            cancellationToken).ConfigureAwait(false);
        try
        {
            snapshot = await _packageIndexCache.BuildSnapshotAsync(
                new PackageIndexBuildRequest
                {
                    ModsRootPath = normalizedRoot,
                    InventoryVersion = inventory.Snapshot.InventoryVersion,
                    PackageFiles = inventory.Snapshot.Entries
                        .Select(entry => new PackageIndexBuildFile
                        {
                            FilePath = entry.PackagePath,
                            Length = entry.FileLength,
                            LastWriteUtcTicks = entry.LastWriteUtcTicks
                        })
                        .ToArray(),
                    ChangedPackageFiles = inventory.AddedEntries
                        .Concat(inventory.ChangedEntries)
                        .Select(entry => new PackageIndexBuildFile
                        {
                            FilePath = entry.PackagePath,
                            Length = entry.FileLength,
                            LastWriteUtcTicks = entry.LastWriteUtcTicks
                        })
                        .ToArray(),
                    RemovedPackagePaths = inventory.RemovedPackagePaths,
                    ParseWorkerCount = parseWorkers,
                    WriteBatchSize = writeBatchSize,
                    ParseResultChannelCapacity = parseResultChannelCapacity,
                    CommitBatchSize = commitBatchSize,
                    CommitIntervalMs = commitIntervalMs,
                    UseAdaptiveThrottleV2 = useAdaptiveThrottleV2,
                    UseIncrementalOrphanCleanup = useIncrementalOrphanCleanup,
                    UseParallelPipeline = useRound2TrayCachePipeline
                },
                new Progress<TrayDependencyExportProgress>(progress =>
                {
                    var stageLabel = BuildTrayCacheStageLabel(progress);
                    if (!string.Equals(currentStageLabel, stageLabel, StringComparison.Ordinal))
                    {
                        if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
                        {
                            session.PublishLog(
                                $"[traycache.stage.done] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
                        }

                        currentStageLabel = stageLabel;
                        stageStopwatch.Restart();
                        session.PublishLog(
                            $"[traycache.stage.start] stage={currentStageLabel} percent={progress.Percent}");
                    }

                    currentStagePercent = progress.Percent;
                    session.PublishProgress(new CacheWarmupProgress
                    {
                        Domain = CacheWarmupDomain.TrayDependency,
                        Stage = progress.Stage.ToString(),
                        Percent = progress.Percent,
                        Current = 0,
                        Total = inventory.Snapshot.Entries.Count,
                        Detail = string.IsNullOrWhiteSpace(progress.Detail)
                            ? "Preparing tray dependency cache..."
                            : progress.Detail,
                        IsBlocking = true
                    });
                    _trayWarmupStates[normalizedRoot] = session.ToStateSnapshot();
                }),
                cancellationToken).ConfigureAwait(false);

            if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
            {
                session.PublishLog(
                    $"[traycache.stage.done] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
            }

            buildTiming.Success(
                "build-done",
                ("packageCount", snapshot.Packages.Count));
        }
        catch (OperationCanceledException)
        {
            if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
            {
                session.PublishLog(
                    $"[traycache.stage.cancel] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
            }

            buildTiming.Cancel("build-cancelled");
            throw;
        }
        catch (Exception ex)
        {
            if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
            {
                session.PublishLog(
                    $"[traycache.stage.fail] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
            }

            buildTiming.Fail(ex, "build-failed");
            throw;
        }

        session.PublishLog(
            $"[traycache.snapshot.build.done] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
        timing.Mark(
            "snapshot.build-done",
            ("packageCount", snapshot.Packages.Count));
        timing.Success(
            "ready",
            ("inventoryVersion", inventory.Snapshot.InventoryVersion),
            ("packageCount", snapshot.Packages.Count));
        return snapshot;
    }

    private WarmupTaskSession<ModPackageInventoryRefreshResult> CreateModsWarmupTaskSession(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory)
    {
        var session = new WarmupTaskSession<ModPackageInventoryRefreshResult>
        {
            WarmupKey = BuildWarmupKey(CacheWarmupDomain.ModsCatalog, normalizedRoot, inventory.Snapshot.InventoryVersion),
            ModsRoot = normalizedRoot,
            InventoryVersion = inventory.Snapshot.InventoryVersion,
            Domain = CacheWarmupDomain.ModsCatalog,
            WorkerCts = new CancellationTokenSource(),
            Task = Task.FromResult(inventory)
        };
        session.MarkRunning("Warmup running.");
        _modsWarmupStates[normalizedRoot] = session.ToStateSnapshot();
        session.Task = RunModsWarmupSessionAsync(normalizedRoot, inventory, session);
        _ = session.Task.ContinueWith(
            _ =>
            {
                session.WorkerCts.Dispose();
                _modsWarmupStates[normalizedRoot] = session.ToStateSnapshot();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return session;
    }

    private WarmupTaskSession<PackageIndexSnapshot> CreateTrayWarmupTaskSession(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory)
    {
        var session = new WarmupTaskSession<PackageIndexSnapshot>
        {
            WarmupKey = BuildWarmupKey(CacheWarmupDomain.TrayDependency, normalizedRoot, inventory.Snapshot.InventoryVersion),
            ModsRoot = normalizedRoot,
            InventoryVersion = inventory.Snapshot.InventoryVersion,
            Domain = CacheWarmupDomain.TrayDependency,
            WorkerCts = new CancellationTokenSource(),
            Task = Task.FromResult(new PackageIndexSnapshot
            {
                ModsRootPath = normalizedRoot,
                InventoryVersion = inventory.Snapshot.InventoryVersion,
                Packages = Array.Empty<IndexedPackageFile>()
            })
        };
        session.MarkRunning("Warmup running.");
        _trayWarmupStates[normalizedRoot] = session.ToStateSnapshot();
        session.Task = RunTrayWarmupSessionAsync(normalizedRoot, inventory, session);
        _ = session.Task.ContinueWith(
            _ =>
            {
                session.WorkerCts.Dispose();
                _trayWarmupStates[normalizedRoot] = session.ToStateSnapshot();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return session;
    }

    private WarmupTaskSession<SavePreviewDescriptorBuildResult> CreateSaveDescriptorWarmupTaskSession(
        string normalizedSavePath,
        string versionToken)
    {
        var session = new WarmupTaskSession<SavePreviewDescriptorBuildResult>
        {
            WarmupKey = BuildSaveDescriptorWarmupKey(normalizedSavePath, versionToken),
            ModsRoot = normalizedSavePath,
            InventoryVersion = 0,
            Domain = CacheWarmupDomain.SavePreviewDescriptor,
            WorkerCts = new CancellationTokenSource(),
            Task = Task.FromResult(new SavePreviewDescriptorBuildResult())
        };
        session.MarkRunning("Warmup running.");
        _saveDescriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
        session.Task = RunSaveDescriptorWarmupSessionAsync(normalizedSavePath, session);
        _ = session.Task.ContinueWith(
            _ =>
            {
                session.WorkerCts.Dispose();
                _saveDescriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return session;
    }

    private WarmupTaskSession<string?> CreateSaveArtifactWarmupTaskSession(
        string normalizedSavePath,
        string householdKey,
        string purpose,
        string versionToken)
    {
        var sessionSource = BuildSaveArtifactSessionSource(normalizedSavePath, householdKey);
        var session = new WarmupTaskSession<string?>
        {
            WarmupKey = BuildSaveArtifactWarmupKey(normalizedSavePath, householdKey, versionToken),
            ModsRoot = sessionSource,
            InventoryVersion = 0,
            Domain = CacheWarmupDomain.SavePreviewArtifact,
            WorkerCts = new CancellationTokenSource(),
            Task = Task.FromResult<string?>(null)
        };
        session.MarkRunning("Warmup running.");
        _saveArtifactWarmupStates[sessionSource] = session.ToStateSnapshot();
        session.Task = RunSaveArtifactWarmupSessionAsync(normalizedSavePath, householdKey, purpose, session);
        _ = session.Task.ContinueWith(
            _ =>
            {
                session.WorkerCts.Dispose();
                _saveArtifactWarmupStates[sessionSource] = session.ToStateSnapshot();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return session;
    }

    private async Task<ModPackageInventoryRefreshResult> RunModsWarmupSessionAsync(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory,
        WarmupTaskSession<ModPackageInventoryRefreshResult> session)
    {
        var changedPackages = inventory.AddedEntries
            .Concat(inventory.ChangedEntries)
            .Select(entry => entry.PackagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using var timing = PerformanceLogScope.Begin(
            _logger,
            "modcache.fastindex",
            ("modsRoot", normalizedRoot),
            ("inventoryVersion", inventory.Snapshot.InventoryVersion),
            ("changedCount", changedPackages.Length),
            ("removedCount", inventory.RemovedPackagePaths.Count));
        session.PublishLog(
            $"[modcache.fastindex.start] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} changed={changedPackages.Length} removed={inventory.RemovedPackagePaths.Count}");

        try
        {
            await _indexScheduler.QueueRefreshAsync(
                new ModIndexRefreshRequest
                {
                    ModsRootPath = normalizedRoot,
                    ChangedPackages = changedPackages,
                    RemovedPackages = inventory.RemovedPackagePaths,
                    AllowDeepEnrichment = false
                },
                new Progress<ModIndexRefreshProgress>(progress =>
                {
                    session.PublishProgress(new CacheWarmupProgress
                    {
                        Domain = CacheWarmupDomain.ModsCatalog,
                        Stage = progress.Stage,
                        Percent = progress.Percent,
                        Current = progress.Current,
                        Total = progress.Total,
                        Detail = string.IsNullOrWhiteSpace(progress.Detail)
                            ? "Preparing Mod catalog cache..."
                            : progress.Detail,
                        IsBlocking = true
                    });
                    _modsWarmupStates[normalizedRoot] = session.ToStateSnapshot();
                }),
                session.WorkerCts.Token).ConfigureAwait(false);

            _modsReadyInventoryVersions[normalizedRoot] = inventory.Snapshot.InventoryVersion;
            var readyProgress = new CacheWarmupProgress
            {
                Domain = CacheWarmupDomain.ModsCatalog,
                Stage = "ready",
                Percent = 100,
                Current = inventory.Snapshot.Entries.Count,
                Total = inventory.Snapshot.Entries.Count,
                Detail = "Mods catalog cache is ready.",
                IsBlocking = true
            };
            session.PublishProgress(readyProgress);
            session.PublishLog(
                $"[modcache.ready] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={inventory.Snapshot.Entries.Count}");
            session.MarkCompleted(readyProgress, "Warmup completed.");
            _modsWarmupStates[normalizedRoot] = session.ToStateSnapshot();
            timing.Success(
                "ready",
                ("packageCount", inventory.Snapshot.Entries.Count),
                ("inventoryVersion", inventory.Snapshot.InventoryVersion));
            return inventory;
        }
        catch (OperationCanceledException) when (session.WorkerCts.IsCancellationRequested)
        {
            session.MarkPaused("Warmup paused.");
            var pausedProgress = BuildPausedProgress(
                CacheWarmupDomain.ModsCatalog,
                session.LastProgress,
                "Mods warmup paused. Switch back to resume.");
            session.PublishProgress(pausedProgress);
            session.PublishLog(
                $"[modcache.fastindex.pause] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion}");
            _modsWarmupStates[normalizedRoot] = session.ToStateSnapshot();
            timing.Cancel("paused");
            throw;
        }
        catch (Exception ex)
        {
            session.MarkFailed(ex, "Warmup failed.");
            _modsWarmupStates[normalizedRoot] = session.ToStateSnapshot();
            timing.Fail(ex, "failed");
            throw;
        }
    }

    private async Task<PackageIndexSnapshot> RunTrayWarmupSessionAsync(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory,
        WarmupTaskSession<PackageIndexSnapshot> session)
    {
        try
        {
            var snapshot = await LoadOrBuildTraySnapshotAsync(
                normalizedRoot,
                inventory,
                session,
                session.WorkerCts.Token).ConfigureAwait(false);

            _trayReadySnapshots[normalizedRoot] = snapshot;
            var readyProgress = new CacheWarmupProgress
            {
                Domain = CacheWarmupDomain.TrayDependency,
                Stage = "ready",
                Percent = 100,
                Current = snapshot.Packages.Count,
                Total = snapshot.Packages.Count,
                Detail = "Tray dependency cache is ready.",
                IsBlocking = true
            };
            session.PublishProgress(readyProgress);
            session.PublishLog(
                $"[traycache.ready] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
            session.MarkCompleted(readyProgress, "Warmup completed.");
            _trayWarmupStates[normalizedRoot] = session.ToStateSnapshot();
            return snapshot;
        }
        catch (OperationCanceledException) when (session.WorkerCts.IsCancellationRequested)
        {
            session.MarkPaused("Warmup paused.");
            var pausedProgress = BuildPausedProgress(
                CacheWarmupDomain.TrayDependency,
                session.LastProgress,
                "Tray warmup paused. Switch back to resume.");
            session.PublishProgress(pausedProgress);
            session.PublishLog(
                $"[traycache.snapshot.pause] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion}");
            _trayWarmupStates[normalizedRoot] = session.ToStateSnapshot();
            throw;
        }
        catch (Exception ex)
        {
            session.MarkFailed(ex, "Warmup failed.");
            _trayWarmupStates[normalizedRoot] = session.ToStateSnapshot();
            throw;
        }
    }

    private async Task<SavePreviewDescriptorBuildResult> RunSaveDescriptorWarmupSessionAsync(
        string normalizedSavePath,
        WarmupTaskSession<SavePreviewDescriptorBuildResult> session)
    {
        try
        {
            var result = await _saveHouseholdCoordinator!
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
                        _saveDescriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
                    }),
                    session.WorkerCts.Token)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error ?? "Failed to build save preview descriptor.");
            }

            var manifest = result.Manifest ?? new SavePreviewDescriptorManifest();
            var readyProgress = new CacheWarmupProgress
            {
                Domain = CacheWarmupDomain.SavePreviewDescriptor,
                Stage = "ready",
                Percent = 100,
                Current = manifest.ReadyHouseholdCount,
                Total = manifest.TotalHouseholdCount,
                Detail = "Save preview descriptor is ready.",
                IsBlocking = true
            };
            session.PublishProgress(readyProgress);
            session.MarkCompleted(readyProgress, "Warmup completed.");
            _saveDescriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
            return result;
        }
        catch (OperationCanceledException) when (session.WorkerCts.IsCancellationRequested)
        {
            session.MarkPaused("Warmup paused.");
            session.PublishProgress(BuildPausedProgress(
                CacheWarmupDomain.SavePreviewDescriptor,
                session.LastProgress,
                "Save preview descriptor warmup paused."));
            _saveDescriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
            throw;
        }
        catch (Exception ex)
        {
            session.MarkFailed(ex, "Warmup failed.");
            _saveDescriptorWarmupStates[normalizedSavePath] = session.ToStateSnapshot();
            throw;
        }
    }

    private async Task<string?> RunSaveArtifactWarmupSessionAsync(
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
            _saveArtifactWarmupStates[sessionSource] = session.ToStateSnapshot();
            _ = await EnsureSavePreviewDescriptorReadyAsync(
                normalizedSavePath,
                CreateDetachedWarmupHost("savepreview.descriptor", purpose),
                session.WorkerCts.Token).ConfigureAwait(false);
            var artifactRoot = await _saveHouseholdCoordinator!
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
            _saveArtifactWarmupStates[sessionSource] = session.ToStateSnapshot();
            return artifactRoot;
        }
        catch (OperationCanceledException) when (session.WorkerCts.IsCancellationRequested)
        {
            session.MarkPaused("Warmup paused.");
            session.PublishProgress(BuildPausedProgress(
                CacheWarmupDomain.SavePreviewArtifact,
                session.LastProgress,
                "Save preview artifact warmup paused."));
            _saveArtifactWarmupStates[sessionSource] = session.ToStateSnapshot();
            throw;
        }
        catch (Exception ex)
        {
            session.MarkFailed(ex, "Warmup failed.");
            _saveArtifactWarmupStates[sessionSource] = session.ToStateSnapshot();
            throw;
        }
    }

    private static CacheWarmupProgress BuildPausedProgress(
        CacheWarmupDomain domain,
        CacheWarmupProgress lastProgress,
        string detail)
    {
        var percent = Math.Clamp(lastProgress.Percent, 0, 99);
        return new CacheWarmupProgress
        {
            Domain = domain,
            Stage = "Paused",
            Percent = percent,
            Current = lastProgress.Current,
            Total = lastProgress.Total,
            Detail = detail,
            IsBlocking = false
        };
    }

    internal void QueueModsPriorityDeepEnrichment(
        string modsRootPath,
        IReadOnlyList<string> priorityPackages,
        Action<string>? appendLog = null)
    {
        if (string.IsNullOrWhiteSpace(modsRootPath) || priorityPackages.Count == 0)
        {
            return;
        }

        var normalizedRoot = ResolveDirectoryPath(modsRootPath);
        if (!_modsReadyInventoryVersions.ContainsKey(normalizedRoot))
        {
            return;
        }

        var priorities = priorityPackages
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(ResolveFilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (priorities.Length == 0)
        {
            return;
        }

        appendLog?.Invoke($"[modcache.deepindex.priority.start] modsRoot={normalizedRoot} priorityCount={priorities.Length}");
        _ = Task.Run(async () =>
        {
            try
            {
                await _indexScheduler.QueueRefreshAsync(
                    new ModIndexRefreshRequest
                    {
                        ModsRootPath = normalizedRoot,
                        PriorityPackages = priorities,
                        AllowDeepEnrichment = true
                    }).ConfigureAwait(false);
                appendLog?.Invoke($"[modcache.deepindex.priority.done] modsRoot={normalizedRoot} priorityCount={priorities.Length}");
            }
            catch (Exception ex)
            {
                appendLog?.Invoke($"[modcache.deepindex.priority.fail] {ex.Message}");
            }
        });
    }

    internal bool TryGetReadyTraySnapshot(string modsRootPath, out PackageIndexSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(modsRootPath))
        {
            return false;
        }

        var normalizedRoot = ResolveDirectoryPath(modsRootPath);
        if (!_trayReadySnapshots.TryGetValue(normalizedRoot, out snapshot))
        {
            return false;
        }
        return true;
    }

    internal void Reset()
    {
        _backgroundCachePrewarmCoordinator?.Reset("warmup-reset");

        foreach (var session in _inventoryRefreshTasks.Values)
        {
            SafeCancelToken(session.WorkerCts);
        }

        foreach (var session in _modsWarmupTasks.Values)
        {
            SafeCancelToken(session.WorkerCts);
        }

        foreach (var session in _trayWarmupTasks.Values)
        {
            SafeCancelToken(session.WorkerCts);
        }

        foreach (var session in _saveDescriptorWarmupTasks.Values)
        {
            SafeCancelToken(session.WorkerCts);
        }

        foreach (var session in _saveArtifactWarmupTasks.Values)
        {
            SafeCancelToken(session.WorkerCts);
        }

        _inventoryResults.Clear();
        _inventoryRefreshTasks.Clear();
        _modsReadyInventoryVersions.Clear();
        _trayReadySnapshots.Clear();
        _modsWarmupTasks.Clear();
        _trayWarmupTasks.Clear();
        _saveDescriptorWarmupTasks.Clear();
        _saveArtifactWarmupTasks.Clear();
        _modsWarmupStates.Clear();
        _trayWarmupStates.Clear();
        _saveDescriptorWarmupStates.Clear();
        _saveArtifactWarmupStates.Clear();
        _rootGates.Clear();
        foreach (var watcher in _rootWatchers.Values)
        {
            try
            {
                watcher.Dispose();
            }
            catch
            {
            }
        }

        _rootWatchers.Clear();
    }

    private SemaphoreSlim GetRootGate(string normalizedRoot)
    {
        return _rootGates.GetOrAdd(normalizedRoot, _ => new SemaphoreSlim(1, 1));
    }

    private async Task<ModPackageInventoryRefreshResult> EnsureInventoryCoreAsync(
        string normalizedRoot,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        if (_inventoryResults.TryGetValue(normalizedRoot, out var cachedInventory))
        {
            EnsureRootWatcher(normalizedRoot);
            return cachedInventory;
        }

        InventoryRefreshTaskSession? session;
        if (_inventoryRefreshTasks.TryGetValue(normalizedRoot, out var inflight))
        {
            session = inflight;
            _logger.LogInformation(
                "modcache.inventory.inflight.reuse modsRoot={ModsRoot}",
                normalizedRoot);
            host.AppendLog($"[modcache.inventory.inflight.reuse] modsRoot={normalizedRoot}");
        }
        else
        {
            var workerCts = new CancellationTokenSource();
            var workerTask = RunInventoryRefreshTaskAsync(normalizedRoot, host, workerCts.Token);
            var candidate = new InventoryRefreshTaskSession
            {
                ModsRoot = normalizedRoot,
                WorkerCts = workerCts,
                Task = workerTask
            };
            if (_inventoryRefreshTasks.TryAdd(normalizedRoot, candidate))
            {
                session = candidate;
                _ = workerTask.ContinueWith(
                    completedTask =>
                    {
                        _inventoryRefreshTasks.TryRemove(normalizedRoot, out InventoryRefreshTaskSession? _);
                        workerCts.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            else
            {
                workerCts.Cancel();
                workerCts.Dispose();
                if (!_inventoryRefreshTasks.TryGetValue(normalizedRoot, out session))
                {
                    return await EnsureInventoryCoreAsync(normalizedRoot, host, cancellationToken).ConfigureAwait(false);
                }
                _logger.LogInformation(
                    "modcache.inventory.inflight.reuse modsRoot={ModsRoot}",
                    normalizedRoot);
                host.AppendLog($"[modcache.inventory.inflight.reuse] modsRoot={normalizedRoot}");
            }
        }

        return await session!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModPackageInventoryRefreshResult> RunInventoryRefreshTaskAsync(
        string normalizedRoot,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        using var timing = PerformanceLogScope.Begin(
            _logger,
            "modcache.inventory",
            ("modsRoot", normalizedRoot));
        host.AppendLog($"[modcache.inventory.start] modsRoot={normalizedRoot}");
        try
        {
            // SqliteModPackageInventoryService.RefreshAsync is mostly synchronous work wrapped in Task.
            // Offload it explicitly to keep UI thread responsive during tray/mod warmup.
            var refreshTask = Task.Run(
                () => _inventoryService.RefreshAsync(
                    normalizedRoot,
                    new Progress<ModPackageInventoryRefreshProgress>(progress =>
                    {
                        host.ReportProgress(new CacheWarmupProgress
                        {
                            Domain = CacheWarmupDomain.ModsCatalog,
                            Stage = progress.Stage,
                            Percent = progress.Percent,
                            Current = progress.Current,
                            Total = progress.Total,
                            Detail = string.IsNullOrWhiteSpace(progress.Detail)
                                ? "Validating package inventory..."
                                : progress.Detail,
                            IsBlocking = true
                        });
                    }),
                    cancellationToken),
                cancellationToken);
            var result = await refreshTask.ConfigureAwait(false);

            _inventoryResults[normalizedRoot] = result;
            EnsureRootWatcher(normalizedRoot);
            if (_trayReadySnapshots.TryGetValue(normalizedRoot, out var readySnapshot) &&
                readySnapshot.InventoryVersion != result.Snapshot.InventoryVersion)
            {
                _trayReadySnapshots.TryRemove(normalizedRoot, out _);
            }

            CleanupStaleWarmupSessions(normalizedRoot, result.Snapshot.InventoryVersion);
            host.AppendLog(
                $"[modcache.inventory.done] modsRoot={normalizedRoot} inventoryVersion={result.Snapshot.InventoryVersion} packages={result.Snapshot.Entries.Count} changed={result.AddedEntries.Count + result.ChangedEntries.Count} removed={result.RemovedPackagePaths.Count}");
            timing.Success(
                "validated",
                ("inventoryVersion", result.Snapshot.InventoryVersion),
                ("packageCount", result.Snapshot.Entries.Count),
                ("changedCount", result.AddedEntries.Count + result.ChangedEntries.Count),
                ("removedCount", result.RemovedPackagePaths.Count));
            return result;
        }
        catch (OperationCanceledException)
        {
            host.AppendLog($"[modcache.inventory.cancel] modsRoot={normalizedRoot}");
            timing.Cancel("inventory cancelled");
            throw;
        }
        catch (Exception ex)
        {
            host.AppendLog($"[modcache.inventory.fail] modsRoot={normalizedRoot} error={ex.Message}");
            timing.Fail(ex, "inventory failed", ("modsRoot", normalizedRoot));
            throw;
        }
    }

    private void CleanupStaleWarmupSessions(string normalizedRoot, long inventoryVersion)
    {
        foreach (var entry in _modsWarmupTasks.ToArray())
        {
            if (!string.Equals(entry.Value.ModsRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Value.InventoryVersion == inventoryVersion)
            {
                continue;
            }

            SafeCancelToken(entry.Value.WorkerCts);
            _modsWarmupTasks.TryRemove(entry.Key, out _);
        }

        foreach (var entry in _trayWarmupTasks.ToArray())
        {
            if (!string.Equals(entry.Value.ModsRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Value.InventoryVersion == inventoryVersion)
            {
                continue;
            }

            SafeCancelToken(entry.Value.WorkerCts);
            _trayWarmupTasks.TryRemove(entry.Key, out _);
        }
    }

    private void EnsureRootWatcher(string normalizedRoot)
    {
        if (string.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
        {
            return;
        }

        _rootWatchers.GetOrAdd(normalizedRoot, root =>
        {
            var watcher = new FileSystemWatcher(root, "*.package")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            watcher.Changed += (_, args) => InvalidateRootCaches(root, "changed", args.FullPath);
            watcher.Created += (_, args) => InvalidateRootCaches(root, "created", args.FullPath);
            watcher.Deleted += (_, args) => InvalidateRootCaches(root, "deleted", args.FullPath);
            watcher.Renamed += (_, args) => InvalidateRootCaches(root, "renamed", args.FullPath);
            watcher.Error += (_, args) =>
            {
                _logger.LogWarning(
                    args.GetException(),
                    "modcache.inventory.watch.error modsRoot={ModsRoot}",
                    root);
                InvalidateRootCaches(root, "watcher-error", string.Empty);
            };
            watcher.EnableRaisingEvents = true;
            _logger.LogInformation("modcache.inventory.watch.start modsRoot={ModsRoot}", root);
            return watcher;
        });
    }

    private void InvalidateRootCaches(string normalizedRoot, string reason, string changedPath)
    {
        _backgroundCachePrewarmCoordinator?.CancelBySource(
            normalizedRoot,
            $"invalidate:{reason}",
            TrayDependencySnapshotPrewarmJobType);
        _backgroundCachePrewarmCoordinator?.CancelBySource(
            normalizedRoot,
            $"invalidate:{reason}",
            ModCatalogQueryPrimeJobType);

        var invalidated = false;
        invalidated |= _inventoryResults.TryRemove(normalizedRoot, out _);
        invalidated |= _modsReadyInventoryVersions.TryRemove(normalizedRoot, out _);
        invalidated |= _trayReadySnapshots.TryRemove(normalizedRoot, out _);
        invalidated |= _modsWarmupStates.TryRemove(normalizedRoot, out _);
        invalidated |= _trayWarmupStates.TryRemove(normalizedRoot, out _);

        if (_inventoryRefreshTasks.TryRemove(normalizedRoot, out var inventorySession))
        {
            SafeCancelToken(inventorySession.WorkerCts);
            invalidated = true;
        }

        foreach (var entry in _modsWarmupTasks.ToArray())
        {
            if (!string.Equals(entry.Value.ModsRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SafeCancelToken(entry.Value.WorkerCts);
            invalidated |= _modsWarmupTasks.TryRemove(entry.Key, out _);
        }

        foreach (var entry in _trayWarmupTasks.ToArray())
        {
            if (!string.Equals(entry.Value.ModsRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SafeCancelToken(entry.Value.WorkerCts);
            invalidated |= _trayWarmupTasks.TryRemove(entry.Key, out _);
        }

        if (!invalidated)
        {
            return;
        }

        _logger.LogInformation(
            "modcache.inventory.invalidate modsRoot={ModsRoot} reason={Reason} changedPath={ChangedPath}",
            normalizedRoot,
            reason,
            changedPath);
    }

    private async Task<bool> GetRound2ConfigBoolAsync(
        string key,
        bool defaultValue,
        CancellationToken cancellationToken)
    {
        if (_configurationProvider is null)
        {
            return defaultValue;
        }

        var configured = await _configurationProvider.GetConfigurationAsync<bool?>(key, cancellationToken).ConfigureAwait(false);
        return configured ?? defaultValue;
    }

    private async Task<int?> GetConfigIntAsync(
        string key,
        int? defaultValue,
        CancellationToken cancellationToken)
    {
        if (_configurationProvider is null)
        {
            return defaultValue;
        }

        var configured = await _configurationProvider.GetConfigurationAsync<int?>(key, cancellationToken).ConfigureAwait(false);
        return configured ?? defaultValue;
    }

    private static string BuildTrayCacheStageLabel(TrayDependencyExportProgress progress)
    {
        var detail = progress.Detail?.Trim() ?? string.Empty;
        if (detail.Length == 0)
        {
            return progress.Stage.ToString();
        }

        var markerIndex = detail.IndexOf("...", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return detail[..(markerIndex + 3)];
        }

        return detail;
    }

    private static string BuildWarmupKey(
        CacheWarmupDomain domain,
        string normalizedRoot,
        long inventoryVersion)
    {
        return domain.ToString() + "|" + normalizedRoot + "|" + inventoryVersion.ToString(CultureInfo.InvariantCulture);
    }

    private static BackgroundPrewarmJobKey BuildTrayPrewarmJobKey(string normalizedRoot)
    {
        return new BackgroundPrewarmJobKey
        {
            JobType = TrayDependencySnapshotPrewarmJobType,
            SourceKey = normalizedRoot
        };
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

    private static string BuildSaveDescriptorWarmupKey(string normalizedSavePath, string versionToken)
    {
        return string.Join(
            "|",
            CacheWarmupDomain.SavePreviewDescriptor,
            normalizedSavePath,
            versionToken);
    }

    private static string BuildSaveArtifactWarmupKey(string normalizedSavePath, string householdKey, string versionToken)
    {
        return string.Join(
            "|",
            CacheWarmupDomain.SavePreviewArtifact,
            normalizedSavePath,
            householdKey,
            versionToken);
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

    private void CancelSaveSessions(string normalizedSavePath, string reason)
    {
        _backgroundCachePrewarmCoordinator?.CancelBySource(
            normalizedSavePath,
            reason,
            SavePreviewDescriptorPrimeJobType);
        _backgroundCachePrewarmCoordinator?.CancelBySource(
            normalizedSavePath,
            reason,
            SavePreviewArtifactPrimeJobType);

        foreach (var entry in _saveDescriptorWarmupTasks.ToArray())
        {
            if (!string.Equals(entry.Value.ModsRoot, normalizedSavePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SafeCancelToken(entry.Value.WorkerCts);
            _saveDescriptorWarmupTasks.TryRemove(entry.Key, out _);
        }

        foreach (var entry in _saveArtifactWarmupTasks.ToArray())
        {
            if (!entry.Value.ModsRoot.StartsWith(normalizedSavePath + SourceKeyScopeSeparator, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SafeCancelToken(entry.Value.WorkerCts);
            _saveArtifactWarmupTasks.TryRemove(entry.Key, out _);
        }

        _saveDescriptorWarmupStates.TryRemove(normalizedSavePath, out _);
        foreach (var key in _saveArtifactWarmupStates.Keys.Where(key =>
                     key.StartsWith(normalizedSavePath + SourceKeyScopeSeparator, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _saveArtifactWarmupStates.TryRemove(key, out _);
        }
    }

    private BackgroundPrewarmJobKey BuildModCatalogPrewarmJobKey(ModItemCatalogQuery query)
    {
        var sourceVersion = new CacheSourceVersion
        {
            SourceKind = "mods-query",
            SourceKey = query.ModsRoot,
            VersionToken = BuildModQueryFingerprint(query)
        };

        return new BackgroundPrewarmJobKey
        {
            JobType = ModCatalogQueryPrimeJobType,
            SourceKey = BuildScopedSourceKey(sourceVersion)
        };
    }

    private async Task PrimeModCatalogQueryAsync(
        ModItemCatalogQuery query,
        string trigger,
        CancellationToken cancellationToken)
    {
        await EnsureModsWorkspaceReadyAsync(
            query.ModsRoot,
            CreateDetachedWarmupHost("modquery", trigger),
            cancellationToken).ConfigureAwait(false);
        _ = await _modItemCatalogService!
            .QueryPageAsync(query, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "modquery.prewarm.done modsRoot={ModsRoot} trigger={Trigger} fingerprint={Fingerprint} pageSize={PageSize}",
            query.ModsRoot,
            trigger,
            BuildModQueryFingerprint(query),
            query.PageSize);
    }

    private MainWindowCacheWarmupHost CreateDetachedWarmupHost(string category, string trigger)
    {
        return new MainWindowCacheWarmupHost
        {
            ReportProgress = _ => { },
            AppendLog = message =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    _logger.LogInformation(
                        "{Category}.prewarm.log trigger={Trigger} message={Message}",
                        category,
                        trigger,
                        message);
                }
            }
        };
    }

    private static void SafeCancelToken(CancellationTokenSource? cancellationTokenSource)
    {
        if (cancellationTokenSource is null)
        {
            return;
        }

        try
        {
            cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private ResolvedPathInfo ResolveDirectory(string path)
    {
        var resolved = _pathIdentityResolver.ResolveDirectory(path);
        var fullPath = !string.IsNullOrWhiteSpace(resolved.FullPath)
            ? resolved.FullPath
            : path.Trim().Trim('"');
        var canonicalPath = !string.IsNullOrWhiteSpace(resolved.CanonicalPath)
            ? resolved.CanonicalPath
            : fullPath;
        return resolved with
        {
            FullPath = fullPath,
            CanonicalPath = canonicalPath
        };
    }

    private string ResolveDirectoryPath(string path)
    {
        return ResolveDirectory(path).CanonicalPath;
    }

    private string ResolveFilePath(string path)
    {
        var resolved = _pathIdentityResolver.ResolveFile(path);
        if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
        {
            return resolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            return resolved.FullPath;
        }

        return path.Trim().Trim('"');
    }

    private ModItemCatalogQuery NormalizeModCatalogQuery(ModItemCatalogQuery query)
    {
        return new ModItemCatalogQuery
        {
            ModsRoot = ResolveDirectoryPath(query.ModsRoot),
            SearchQuery = query.SearchQuery?.Trim() ?? string.Empty,
            EntityKindFilter = NormalizeQueryValue(query.EntityKindFilter, "All"),
            SubTypeFilter = NormalizeQueryValue(query.SubTypeFilter, "All"),
            SortBy = NormalizeQueryValue(query.SortBy, "Last Indexed"),
            PageIndex = Math.Max(1, query.PageIndex),
            PageSize = Math.Max(1, query.PageSize)
        };
    }

    private static string BuildModQueryFingerprint(ModItemCatalogQuery query)
    {
        return string.Join(
            SourceKeyScopeSeparator,
            query.EntityKindFilter,
            query.SubTypeFilter,
            query.SortBy,
            query.SearchQuery,
            query.PageIndex.ToString(CultureInfo.InvariantCulture),
            query.PageSize.ToString(CultureInfo.InvariantCulture));
    }

    private static string BuildScopedSourceKey(CacheSourceVersion sourceVersion)
    {
        return string.Join(
            SourceKeyScopeSeparator,
            sourceVersion.SourceKey,
            sourceVersion.SourceKind,
            sourceVersion.VersionToken);
    }

    private static string NormalizeQueryValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}
