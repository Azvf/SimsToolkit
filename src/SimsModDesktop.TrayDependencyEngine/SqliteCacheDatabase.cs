using Microsoft.Data.Sqlite;

namespace SimsModDesktop.TrayDependencyEngine;

internal sealed class SqliteCacheDatabase
{
    private readonly string _databasePath;

    public SqliteCacheDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    public SqliteConnection OpenConnection()
    {
        EnsureParentDirectory();
        var connection = new SqliteConnection(BuildConnectionString());
        connection.Open();
        ConfigureConnection(connection);
        return connection;
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        EnsureParentDirectory();
        var connection = new SqliteConnection(BuildConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        ConfigureConnection(connection);
        return connection;
    }

    public string DatabasePath => _databasePath;

    private void EnsureParentDirectory()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private string BuildConnectionString()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };

        return builder.ToString();
    }

    private static void ConfigureConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            """;
        command.ExecuteNonQuery();
    }
}
