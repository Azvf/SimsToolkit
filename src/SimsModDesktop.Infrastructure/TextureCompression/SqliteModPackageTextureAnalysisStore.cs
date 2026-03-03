using Dapper;
using SimsModDesktop.Infrastructure.Persistence;
using System.Text.Json;

namespace SimsModDesktop.Application.TextureCompression;

public sealed class SqliteModPackageTextureAnalysisStore : IModPackageTextureAnalysisStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppCacheDatabase _database;

    public SqliteModPackageTextureAnalysisStore()
        : this(new AppCacheDatabase())
    {
    }

    public SqliteModPackageTextureAnalysisStore(string cacheRootPath)
        : this(new AppCacheDatabase(cacheRootPath))
    {
    }

    private SqliteModPackageTextureAnalysisStore(AppCacheDatabase database)
    {
        _database = database;
    }

    public Task<ModPackageTextureAnalysisResult?> TryGetAsync(
        string packagePath,
        long fileLength,
        long lastWriteUtcTicks,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        using var connection = OpenConnection();
        var row = connection.QuerySingleOrDefault<ModTextureAnalysisRow>(
            """
            SELECT
                PackagePath,
                FileLength,
                LastWriteUtcTicks,
                TextureResourceCount,
                DdsCount,
                PngCount,
                UnsupportedTextureCount,
                EditableTextureCount,
                TotalTextureBytes,
                LastAnalyzedLocalTicks,
                CandidatesJson
            FROM ModTextureAnalysisCache
            WHERE PackagePath = @PackagePath
              AND FileLength = @FileLength
              AND LastWriteUtcTicks = @LastWriteUtcTicks
            LIMIT 1;
            """,
            new
            {
                PackagePath = Path.GetFullPath(packagePath.Trim()),
                FileLength = fileLength,
                LastWriteUtcTicks = lastWriteUtcTicks
            });

        if (row is null)
        {
            return Task.FromResult<ModPackageTextureAnalysisResult?>(null);
        }

        var summary = new ModPackageTextureSummary
        {
            PackagePath = row.PackagePath,
            FileLength = row.FileLength,
            LastWriteUtcTicks = row.LastWriteUtcTicks,
            TextureResourceCount = row.TextureResourceCount,
            DdsCount = row.DdsCount,
            PngCount = row.PngCount,
            UnsupportedTextureCount = row.UnsupportedTextureCount,
            EditableTextureCount = row.EditableTextureCount,
            TotalTextureBytes = row.TotalTextureBytes,
            LastAnalyzedLocal = new DateTime(row.LastAnalyzedLocalTicks, DateTimeKind.Local)
        };
        var candidates = JsonSerializer.Deserialize<List<ModPackageTextureCandidate>>(row.CandidatesJson, JsonOptions) ?? [];
        return Task.FromResult<ModPackageTextureAnalysisResult?>(new ModPackageTextureAnalysisResult
        {
            Summary = summary,
            Candidates = candidates
        });
    }

    public Task SaveAsync(ModPackageTextureAnalysisResult analysis, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(analysis);
        var summary = analysis.Summary;

        using var connection = OpenConnection();
        connection.Execute(
            """
            INSERT INTO ModTextureAnalysisCache (
                PackagePath,
                FileLength,
                LastWriteUtcTicks,
                TextureResourceCount,
                DdsCount,
                PngCount,
                UnsupportedTextureCount,
                EditableTextureCount,
                TotalTextureBytes,
                LastAnalyzedLocalTicks,
                CandidatesJson,
                UpdatedUtcTicks
            )
            VALUES (
                @PackagePath,
                @FileLength,
                @LastWriteUtcTicks,
                @TextureResourceCount,
                @DdsCount,
                @PngCount,
                @UnsupportedTextureCount,
                @EditableTextureCount,
                @TotalTextureBytes,
                @LastAnalyzedLocalTicks,
                @CandidatesJson,
                @UpdatedUtcTicks
            )
            ON CONFLICT(PackagePath) DO UPDATE SET
                FileLength = excluded.FileLength,
                LastWriteUtcTicks = excluded.LastWriteUtcTicks,
                TextureResourceCount = excluded.TextureResourceCount,
                DdsCount = excluded.DdsCount,
                PngCount = excluded.PngCount,
                UnsupportedTextureCount = excluded.UnsupportedTextureCount,
                EditableTextureCount = excluded.EditableTextureCount,
                TotalTextureBytes = excluded.TotalTextureBytes,
                LastAnalyzedLocalTicks = excluded.LastAnalyzedLocalTicks,
                CandidatesJson = excluded.CandidatesJson,
                UpdatedUtcTicks = excluded.UpdatedUtcTicks;
            """,
            new
            {
                summary.PackagePath,
                summary.FileLength,
                summary.LastWriteUtcTicks,
                summary.TextureResourceCount,
                summary.DdsCount,
                summary.PngCount,
                summary.UnsupportedTextureCount,
                summary.EditableTextureCount,
                summary.TotalTextureBytes,
                LastAnalyzedLocalTicks = summary.LastAnalyzedLocal.Ticks,
                CandidatesJson = JsonSerializer.Serialize(analysis.Candidates, JsonOptions),
                UpdatedUtcTicks = DateTime.UtcNow.Ticks
            });

        return Task.CompletedTask;
    }

    private System.Data.IDbConnection OpenConnection()
    {
        var connection = _database.OpenConnection();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS ModTextureAnalysisCache (
                PackagePath TEXT PRIMARY KEY,
                FileLength INTEGER NOT NULL,
                LastWriteUtcTicks INTEGER NOT NULL,
                TextureResourceCount INTEGER NOT NULL,
                DdsCount INTEGER NOT NULL,
                PngCount INTEGER NOT NULL,
                UnsupportedTextureCount INTEGER NOT NULL,
                EditableTextureCount INTEGER NOT NULL,
                TotalTextureBytes INTEGER NOT NULL,
                LastAnalyzedLocalTicks INTEGER NOT NULL,
                CandidatesJson TEXT NOT NULL DEFAULT '[]',
                UpdatedUtcTicks INTEGER NOT NULL
            );
            """);
        return connection;
    }

    private sealed class ModTextureAnalysisRow
    {
        public string PackagePath { get; set; } = string.Empty;
        public long FileLength { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public int TextureResourceCount { get; set; }
        public int DdsCount { get; set; }
        public int PngCount { get; set; }
        public int UnsupportedTextureCount { get; set; }
        public int EditableTextureCount { get; set; }
        public long TotalTextureBytes { get; set; }
        public long LastAnalyzedLocalTicks { get; set; }
        public string CandidatesJson { get; set; } = "[]";
    }
}
