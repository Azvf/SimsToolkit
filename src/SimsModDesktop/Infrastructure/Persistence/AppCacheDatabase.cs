using Microsoft.Data.Sqlite;

namespace SimsModDesktop.Infrastructure.Persistence;

internal sealed class AppCacheDatabase
{
    private readonly string _databasePath;

    public AppCacheDatabase()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache"))
    {
    }

    public AppCacheDatabase(string cacheRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRootPath);
        _databasePath = Path.Combine(cacheRootPath, "app-cache.db");
    }

    public SqliteConnection OpenConnection()
    {
        EnsureDatabasePathIsReady();

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            """;
        command.ExecuteNonQuery();

        return connection;
    }

    private void EnsureDatabasePathIsReady()
    {
        if (!Directory.Exists(_databasePath))
        {
            return;
        }

        if (!Directory.EnumerateFileSystemEntries(_databasePath).Any())
        {
            Directory.Delete(_databasePath, recursive: false);
            return;
        }

        var backupPath = $"{_databasePath}.dir-backup-{DateTime.UtcNow:yyyyMMddHHmmss}";
        Directory.Move(_databasePath, backupPath);
    }
}
