using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace SimsModDesktop.Infrastructure.Services;

public sealed class AppCacheMaintenanceService : IAppCacheMaintenanceService
{
    private readonly string _cacheRootPath;

    public AppCacheMaintenanceService()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache"))
    {
    }

    public AppCacheMaintenanceService(string cacheRootPath)
    {
        _cacheRootPath = cacheRootPath;
    }

    public Task<AppCacheMaintenanceResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        return ClearInternalAsync(includePackageIndexCache: false, cancellationToken);
    }

    public Task<AppCacheMaintenanceResult> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        return ClearInternalAsync(includePackageIndexCache: true, cancellationToken);
    }

    public Task<AppCacheMaintenanceResult> MaintainAsync(
        AppCacheMaintenanceMode mode = AppCacheMaintenanceMode.Light,
        bool includePackageIndexCache = true,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            var details = new List<AppCacheDatabaseMaintenanceDetail>();
            var databaseTargets = new List<string>
            {
                Path.Combine(_cacheRootPath, "app-cache.db")
            };

            if (includePackageIndexCache)
            {
                databaseTargets.Add(Path.Combine(_cacheRootPath, "TrayDependencyPackageIndex", "cache.db"));
            }

            foreach (var databasePath in databaseTargets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                details.Add(MaintainDatabase(databasePath, mode, cancellationToken));
            }

            stopwatch.Stop();

            var maintainedCount = details.Count(item => item.Success);
            var failedCount = details.Count(item => !item.Success);
            var hasAnyTarget = details.Count > 0;

            var message = !hasAnyTarget
                ? "No SQLite cache databases are configured for maintenance."
                : failedCount == 0
                    ? $"Maintained {maintainedCount} SQLite cache database(s) in {stopwatch.ElapsedMilliseconds} ms."
                    : $"Maintained {maintainedCount} SQLite cache database(s); {failedCount} failed.";

            return new AppCacheMaintenanceResult
            {
                Success = failedCount == 0,
                MaintenanceMode = mode,
                MaintainedDatabaseCount = maintainedCount,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                DatabaseDetails = details,
                Message = message
            };
        }, cancellationToken);
    }

    private Task<AppCacheMaintenanceResult> ClearInternalAsync(bool includePackageIndexCache, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var removedDirectoryCount = 0;
            var removedFileCount = 0;
            var directoryTargets = new[]
            {
                Path.Combine(_cacheRootPath, "SavePreview"),
                Path.Combine(_cacheRootPath, "TrayPreviewThumbnails", "thumbs")
            };
            var fileTargets = new[]
            {
                Path.Combine(_cacheRootPath, "app-cache.db"),
                Path.Combine(_cacheRootPath, "app-cache.db-wal"),
                Path.Combine(_cacheRootPath, "app-cache.db-shm")
            }.ToList();

            if (includePackageIndexCache)
            {
                fileTargets.Add(Path.Combine(_cacheRootPath, "TrayDependencyPackageIndex", "cache.db"));
                fileTargets.Add(Path.Combine(_cacheRootPath, "TrayDependencyPackageIndex", "cache.db-wal"));
                fileTargets.Add(Path.Combine(_cacheRootPath, "TrayDependencyPackageIndex", "cache.db-shm"));
            }

            try
            {
                foreach (var path in fileTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    File.Delete(path);
                    removedFileCount++;
                }

                foreach (var path in directoryTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Directory.Exists(path))
                    {
                        continue;
                    }

                    Directory.Delete(path, recursive: true);
                    removedDirectoryCount++;
                }

                if (Directory.Exists(_cacheRootPath) &&
                    !Directory.EnumerateFileSystemEntries(_cacheRootPath).Any())
                {
                    Directory.Delete(_cacheRootPath, recursive: false);
                }

                var removedTargetCount = removedDirectoryCount + removedFileCount;
                var message = removedTargetCount > 0
                    ? $"Cleared {removedDirectoryCount} cache folder(s) and {removedFileCount} cache file(s). Restart the app to drop in-memory caches."
                    : "No disk cache files were present. Restart the app to drop in-memory caches.";

                return new AppCacheMaintenanceResult
                {
                    Success = true,
                    RemovedDirectoryCount = removedDirectoryCount,
                    RemovedFileCount = removedFileCount,
                    Message = message
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new AppCacheMaintenanceResult
                {
                    Success = false,
                    RemovedDirectoryCount = removedDirectoryCount,
                    RemovedFileCount = removedFileCount,
                    Message = $"Failed to clear cache: {ex.Message}"
                };
            }
        }, cancellationToken);
    }

    private static AppCacheDatabaseMaintenanceDetail MaintainDatabase(
        string databasePath,
        AppCacheMaintenanceMode mode,
        CancellationToken cancellationToken)
    {
        var walPath = $"{databasePath}-wal";
        var dbBefore = GetFileSize(databasePath);
        var walBefore = GetFileSize(walPath);

        if (!File.Exists(databasePath))
        {
            return new AppCacheDatabaseMaintenanceDetail
            {
                DatabasePath = databasePath,
                Success = true,
                DatabaseBytesBefore = dbBefore,
                DatabaseBytesAfter = dbBefore,
                WalBytesBefore = walBefore,
                WalBytesAfter = walBefore,
                Message = "Database file does not exist; skipped."
            };
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            };

            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();

            command.CommandText = "PRAGMA optimize;";
            command.ExecuteNonQuery();

            if (mode >= AppCacheMaintenanceMode.Standard)
            {
                command.CommandText = "ANALYZE;";
                command.ExecuteNonQuery();
            }

            if (mode == AppCacheMaintenanceMode.Deep)
            {
                command.CommandText = "VACUUM;";
                command.ExecuteNonQuery();
            }

            var dbAfter = GetFileSize(databasePath);
            var walAfter = GetFileSize(walPath);
            return new AppCacheDatabaseMaintenanceDetail
            {
                DatabasePath = databasePath,
                Success = true,
                DatabaseBytesBefore = dbBefore,
                DatabaseBytesAfter = dbAfter,
                WalBytesBefore = walBefore,
                WalBytesAfter = walAfter,
                Message = mode switch
                {
                    AppCacheMaintenanceMode.Light => "Applied wal_checkpoint(TRUNCATE) + optimize.",
                    AppCacheMaintenanceMode.Standard => "Applied wal_checkpoint(TRUNCATE) + optimize + analyze.",
                    _ => "Applied wal_checkpoint(TRUNCATE) + optimize + analyze + vacuum."
                }
            };
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            var dbAfter = GetFileSize(databasePath);
            var walAfter = GetFileSize(walPath);
            return new AppCacheDatabaseMaintenanceDetail
            {
                DatabasePath = databasePath,
                Success = false,
                DatabaseBytesBefore = dbBefore,
                DatabaseBytesAfter = dbAfter,
                WalBytesBefore = walBefore,
                WalBytesAfter = walAfter,
                Message = ex.Message
            };
        }
    }

    private static long GetFileSize(string path)
    {
        return File.Exists(path)
            ? new FileInfo(path).Length
            : 0;
    }
}
