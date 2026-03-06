using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.PackageCore;
using SimsModDesktop.Presentation.ViewModels;

namespace SimsModDesktop.Presentation.Services;

public sealed class AppIdlePrewarmBootstrapper
{
    private const string StartupTrayPrewarmJobType = "StartupTrayDependencyPrewarm";
    private const string StartupModsQueryPrewarmJobType = "StartupModsQueryPrewarm";
    private const string StartupSaveDescriptorPrewarmJobType = "StartupSaveDescriptorPrewarm";
    private const string StartupSaveArtifactPrewarmJobType = "StartupSaveArtifactPrewarm";
    private readonly MainWindowCacheWarmupController _cacheWarmupController;
    private readonly IUiActivityMonitor _uiActivityMonitor;
    private readonly IConfigurationProvider? _configurationProvider;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private readonly ILogger<AppIdlePrewarmBootstrapper> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _scheduledJobs = new(StringComparer.OrdinalIgnoreCase);

    public AppIdlePrewarmBootstrapper(
        MainWindowCacheWarmupController cacheWarmupController,
        IUiActivityMonitor uiActivityMonitor,
        ILogger<AppIdlePrewarmBootstrapper>? logger = null,
        IConfigurationProvider? configurationProvider = null,
        IPathIdentityResolver? pathIdentityResolver = null)
    {
        _cacheWarmupController = cacheWarmupController;
        _uiActivityMonitor = uiActivityMonitor;
        _logger = logger ?? NullLogger<AppIdlePrewarmBootstrapper>.Instance;
        _configurationProvider = configurationProvider;
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
    }

    public void QueueTrayDependencyStartupPrewarm(
        string modsRootPath,
        Func<bool>? isForegroundBusy = null)
    {
        QueueStartupPrewarm(
            StartupTrayPrewarmJobType,
            modsRootPath,
            () => GetConfigBool("Performance.IdlePrewarm.Enabled", true),
            normalizedRoot => _cacheWarmupController.QueueTrayDependencyIdlePrewarm(normalizedRoot, "startup-idle"),
            isForegroundBusy,
            "traycache.idleprewarm.schedule.fail");
    }

    public void QueueModCatalogStartupPrewarm(
        ModItemCatalogQuery query,
        Func<bool>? isForegroundBusy = null)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueueStartupPrewarm(
            StartupModsQueryPrewarmJobType,
            query.ModsRoot,
            () => GetConfigBool("Performance.IdlePrewarm.Enabled", true) &&
                  GetConfigBool("Performance.IdlePrewarm.ModQueryPrimeEnabled", true),
            normalizedRoot => _cacheWarmupController.QueueModsQueryIdlePrewarm(
                new ModItemCatalogQuery
                {
                    ModsRoot = normalizedRoot,
                    SearchQuery = query.SearchQuery,
                    EntityKindFilter = query.EntityKindFilter,
                    SubTypeFilter = query.SubTypeFilter,
                    SortBy = query.SortBy,
                    PageIndex = Math.Max(1, query.PageIndex),
                    PageSize = Math.Max(1, query.PageSize)
                },
                "startup-idle"),
            isForegroundBusy,
            "modquery.idleprewarm.schedule.fail");
    }

    public void QueueSavePreviewStartupPrewarm(
        string saveFilePath,
        string? selectedHouseholdKey,
        Func<bool>? isForegroundBusy = null)
    {
        QueueStartupFilePrewarm(
            StartupSaveDescriptorPrewarmJobType,
            saveFilePath,
            () => GetConfigBool("Performance.IdlePrewarm.Enabled", true) &&
                  GetConfigBool("Performance.IdlePrewarm.SaveDescriptorPrimeEnabled", true),
            normalizedSavePath => _cacheWarmupController.QueueSavePreviewDescriptorIdlePrewarm(normalizedSavePath, "startup-idle"),
            isForegroundBusy,
            "savepreview.descriptor.idleprewarm.schedule.fail");

        if (string.IsNullOrWhiteSpace(selectedHouseholdKey) ||
            !GetConfigBool("Performance.IdlePrewarm.SaveArtifactPrimeEnabled", true))
        {
            return;
        }

        QueueStartupFilePrewarm(
            StartupSaveArtifactPrewarmJobType,
            saveFilePath,
            () => GetConfigBool("Performance.IdlePrewarm.Enabled", true) &&
                  GetConfigBool("Performance.IdlePrewarm.SaveArtifactPrimeEnabled", true),
            normalizedSavePath => _cacheWarmupController.QueueSavePreviewArtifactIdlePrewarm(
                normalizedSavePath,
                selectedHouseholdKey.Trim(),
                "startup-idle"),
            isForegroundBusy,
            "savepreview.artifact.idleprewarm.schedule.fail",
            scheduleKeySuffix: selectedHouseholdKey.Trim());
    }

    public void Reset()
    {
        lock (_gate)
        {
            foreach (var cts in _scheduledJobs.Values)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            _scheduledJobs.Clear();
        }
    }

    private void QueueStartupPrewarm(
        string jobType,
        string sourcePath,
        Func<bool> isEnabled,
        Func<string, bool> queueWork,
        Func<bool>? isForegroundBusy,
        string failureLogName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        var resolved = _pathIdentityResolver.ResolveDirectory(sourcePath);
        var normalizedRoot = !string.IsNullOrWhiteSpace(resolved.CanonicalPath)
            ? resolved.CanonicalPath
            : resolved.FullPath;
        if (string.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
        {
            return;
        }

        if (!isEnabled())
        {
            return;
        }

        var busyProbe = isForegroundBusy ?? (() => false);
        var scheduleKey = jobType + "|" + normalizedRoot;
        lock (_gate)
        {
            if (_scheduledJobs.ContainsKey(scheduleKey))
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _scheduledJobs[scheduleKey] = cts;
            _ = Task.Run(
                () => RunScheduledStartupPrewarmAsync(
                    scheduleKey,
                    normalizedRoot,
                    busyProbe,
                    queueWork,
                    cts,
                    failureLogName),
                CancellationToken.None);
        }
    }

    private void QueueStartupFilePrewarm(
        string jobType,
        string sourcePath,
        Func<bool> isEnabled,
        Func<string, bool> queueWork,
        Func<bool>? isForegroundBusy,
        string failureLogName,
        string? scheduleKeySuffix = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        var resolved = _pathIdentityResolver.ResolveFile(sourcePath);
        var normalizedFile = !string.IsNullOrWhiteSpace(resolved.CanonicalPath)
            ? resolved.CanonicalPath
            : resolved.FullPath;
        if (string.IsNullOrWhiteSpace(normalizedFile) || !File.Exists(normalizedFile))
        {
            return;
        }

        if (!isEnabled())
        {
            return;
        }

        var busyProbe = isForegroundBusy ?? (() => false);
        var scheduleKey = string.IsNullOrWhiteSpace(scheduleKeySuffix)
            ? jobType + "|" + normalizedFile
            : jobType + "|" + normalizedFile + "|" + scheduleKeySuffix;
        lock (_gate)
        {
            if (_scheduledJobs.ContainsKey(scheduleKey))
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _scheduledJobs[scheduleKey] = cts;
            _ = Task.Run(
                () => RunScheduledStartupPrewarmAsync(
                    scheduleKey,
                    normalizedFile,
                    busyProbe,
                    queueWork,
                    cts,
                    failureLogName,
                    validateDirectory: false),
                CancellationToken.None);
        }
    }

    private async Task RunScheduledStartupPrewarmAsync(
        string scheduleKey,
        string normalizedPath,
        Func<bool> isForegroundBusy,
        Func<string, bool> queueWork,
        CancellationTokenSource cts,
        string failureLogName,
        bool validateDirectory = true)
    {
        try
        {
            var idleDelayMs = Math.Max(1000, GetConfigInt("Performance.IdlePrewarm.DelayMs", 10000));
            while (!cts.IsCancellationRequested)
            {
                if (validateDirectory ? !Directory.Exists(normalizedPath) : !File.Exists(normalizedPath))
                {
                    return;
                }

                if (!isForegroundBusy() &&
                    DateTimeOffset.UtcNow - _uiActivityMonitor.LastInteractionUtc >= TimeSpan.FromMilliseconds(idleDelayMs))
                {
                    _ = queueWork(normalizedPath);
                    return;
                }

                await Task.Delay(500, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "{FailureLogName} modsRoot={ModsRoot}",
                failureLogName,
                normalizedPath);
        }
        finally
        {
            lock (_gate)
            {
                if (_scheduledJobs.TryGetValue(scheduleKey, out var current) && ReferenceEquals(current, cts))
                {
                    _scheduledJobs.Remove(scheduleKey);
                }
            }

            cts.Dispose();
        }
    }

    private bool GetConfigBool(string key, bool defaultValue)
    {
        if (_configurationProvider is null)
        {
            return defaultValue;
        }

        return _configurationProvider.GetConfigurationAsync<bool?>(key).GetAwaiter().GetResult() ?? defaultValue;
    }

    private int GetConfigInt(string key, int defaultValue)
    {
        if (_configurationProvider is null)
        {
            return defaultValue;
        }

        return _configurationProvider.GetConfigurationAsync<int?>(key).GetAwaiter().GetResult() ?? defaultValue;
    }
}
