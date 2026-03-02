using Dapper;
using SimsModDesktop.Infrastructure.Persistence;

namespace SimsModDesktop.Application.TextureCompression;

public sealed class SqliteModPackageTextureEditStore : IModPackageTextureEditStore
{
    private readonly AppCacheDatabase _database;

    public SqliteModPackageTextureEditStore()
        : this(new AppCacheDatabase())
    {
    }

    public SqliteModPackageTextureEditStore(string cacheRootPath)
        : this(new AppCacheDatabase(cacheRootPath))
    {
    }

    private SqliteModPackageTextureEditStore(AppCacheDatabase database)
    {
        _database = database;
    }

    public Task SaveAsync(ModPackageTextureEditRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);

        using var connection = OpenConnection();
        connection.Execute(
            """
            INSERT INTO ModTextureEditHistory (
                EditId,
                PackagePath,
                ResourceKeyText,
                RecordKind,
                AppliedAction,
                OriginalBytes,
                ReplacementBytes,
                AppliedUtcTicks,
                TargetEditId,
                RolledBackUtcTicks,
                Notes
            )
            VALUES (
                @EditId,
                @PackagePath,
                @ResourceKeyText,
                @RecordKind,
                @AppliedAction,
                @OriginalBytes,
                @ReplacementBytes,
                @AppliedUtcTicks,
                @TargetEditId,
                @RolledBackUtcTicks,
                @Notes
            );
            """,
            new
            {
                record.EditId,
                PackagePath = Path.GetFullPath(record.PackagePath),
                record.ResourceKeyText,
                record.RecordKind,
                record.AppliedAction,
                record.OriginalBytes,
                record.ReplacementBytes,
                record.AppliedUtcTicks,
                record.TargetEditId,
                record.RolledBackUtcTicks,
                record.Notes
            });

        return Task.CompletedTask;
    }

    public Task<ModPackageTextureEditRecord?> TryGetLatestActiveEditAsync(
        string packagePath,
        string resourceKeyText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKeyText);

        using var connection = OpenConnection();
        var row = connection.QuerySingleOrDefault<ModTextureEditRow>(
            """
            SELECT
                EditId,
                PackagePath,
                ResourceKeyText,
                RecordKind,
                AppliedAction,
                OriginalBytes,
                ReplacementBytes,
                AppliedUtcTicks,
                TargetEditId,
                RolledBackUtcTicks,
                Notes
            FROM ModTextureEditHistory
            WHERE PackagePath = @PackagePath
              AND ResourceKeyText = @ResourceKeyText
              AND RecordKind = 'Apply'
              AND RolledBackUtcTicks IS NULL
            ORDER BY AppliedUtcTicks DESC
            LIMIT 1;
            """,
            new
            {
                PackagePath = Path.GetFullPath(packagePath),
                ResourceKeyText = resourceKeyText.Trim()
            });

        return Task.FromResult(row is null ? null : ToRecord(row));
    }

    public Task<IReadOnlyList<ModPackageTextureEditRecord>> GetHistoryAsync(
        string packagePath,
        string resourceKeyText,
        int maxCount = 10,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKeyText);
        maxCount = Math.Max(1, maxCount);

        using var connection = OpenConnection();
        var rows = connection.Query<ModTextureEditRow>(
            """
            SELECT
                EditId,
                PackagePath,
                ResourceKeyText,
                RecordKind,
                AppliedAction,
                OriginalBytes,
                ReplacementBytes,
                AppliedUtcTicks,
                TargetEditId,
                RolledBackUtcTicks,
                Notes
            FROM ModTextureEditHistory
            WHERE PackagePath = @PackagePath
              AND ResourceKeyText = @ResourceKeyText
            ORDER BY AppliedUtcTicks DESC
            LIMIT @MaxCount;
            """,
            new
            {
                PackagePath = Path.GetFullPath(packagePath),
                ResourceKeyText = resourceKeyText.Trim(),
                MaxCount = maxCount
            });

        return Task.FromResult<IReadOnlyList<ModPackageTextureEditRecord>>(rows.Select(ToRecord).ToArray());
    }

    public Task MarkRolledBackAsync(string editId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(editId);

        using var connection = OpenConnection();
        connection.Execute(
            """
            UPDATE ModTextureEditHistory
            SET RolledBackUtcTicks = @RolledBackUtcTicks
            WHERE EditId = @EditId;
            """,
            new
            {
                EditId = editId.Trim(),
                RolledBackUtcTicks = DateTime.UtcNow.Ticks
            });

        return Task.CompletedTask;
    }

    private System.Data.IDbConnection OpenConnection()
    {
        var connection = _database.OpenConnection();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS ModTextureEditHistory (
                EditId TEXT PRIMARY KEY,
                PackagePath TEXT NOT NULL,
                ResourceKeyText TEXT NOT NULL,
                RecordKind TEXT NOT NULL,
                AppliedAction TEXT NOT NULL,
                OriginalBytes BLOB NOT NULL,
                ReplacementBytes BLOB NOT NULL,
                AppliedUtcTicks INTEGER NOT NULL,
                TargetEditId TEXT NULL,
                RolledBackUtcTicks INTEGER NULL,
                Notes TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ModTextureEditHistory_PackageResource
            ON ModTextureEditHistory (PackagePath, ResourceKeyText, AppliedUtcTicks DESC);
            """);
        return connection;
    }

    private static ModPackageTextureEditRecord ToRecord(ModTextureEditRow row)
    {
        return new ModPackageTextureEditRecord
        {
            EditId = row.EditId,
            PackagePath = row.PackagePath,
            ResourceKeyText = row.ResourceKeyText,
            RecordKind = row.RecordKind,
            AppliedAction = row.AppliedAction,
            OriginalBytes = row.OriginalBytes,
            ReplacementBytes = row.ReplacementBytes,
            AppliedUtcTicks = row.AppliedUtcTicks,
            TargetEditId = row.TargetEditId,
            RolledBackUtcTicks = row.RolledBackUtcTicks,
            Notes = row.Notes
        };
    }

    private sealed class ModTextureEditRow
    {
        public string EditId { get; set; } = string.Empty;
        public string PackagePath { get; set; } = string.Empty;
        public string ResourceKeyText { get; set; } = string.Empty;
        public string RecordKind { get; set; } = string.Empty;
        public string AppliedAction { get; set; } = string.Empty;
        public byte[] OriginalBytes { get; set; } = Array.Empty<byte>();
        public byte[] ReplacementBytes { get; set; } = Array.Empty<byte>();
        public long AppliedUtcTicks { get; set; }
        public string? TargetEditId { get; set; }
        public long? RolledBackUtcTicks { get; set; }
        public string? Notes { get; set; }
    }
}
