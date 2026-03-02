using System.ComponentModel;
using System.Text.Json;
using Dapper;
using SimsModDesktop.Infrastructure.Persistence;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Results;

public sealed class ActionResultRepository : IActionResultRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly List<ActionResultEnvelope> _history = [];
    private readonly AppCacheDatabase _database;
    private ActionResultEnvelope? _latest;

    public ActionResultRepository()
        : this(new AppCacheDatabase())
    {
    }

    internal ActionResultRepository(string cacheRootPath)
        : this(new AppCacheDatabase(cacheRootPath))
    {
    }

    private ActionResultRepository(AppCacheDatabase database)
    {
        _database = database;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ActionResultEnvelope? Latest => _latest;
    public IReadOnlyList<ActionResultEnvelope> History => _history;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _history.Clear();
        using var connection = OpenConnection();
        var rows = connection.Query<ActionResultHistoryRow>(
            """
            SELECT
                ResultId,
                Action,
                Source,
                GeneratedAtLocalTicks,
                RowsJson,
                RelatedOperationId,
                InsertedUtcTicks
            FROM ActionResultHistory
            ORDER BY InsertedUtcTicks DESC
            LIMIT 20;
            """);

        foreach (var row in rows)
        {
            var envelope = new ActionResultEnvelope
            {
                Action = (SimsAction)row.Action,
                Source = row.Source,
                GeneratedAtLocal = new DateTime(row.GeneratedAtLocalTicks, DateTimeKind.Local),
                Rows = JsonSerializer.Deserialize<List<ActionResultRow>>(row.RowsJson, JsonOptions) ?? []
            };
            _history.Add(envelope);
        }

        _latest = _history.FirstOrDefault();
        Raise(nameof(Latest));
        Raise(nameof(History));
        return Task.CompletedTask;
    }

    public Task SaveAsync(ActionResultEnvelope envelope, string? relatedOperationId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);

        _latest = envelope;
        _history.Insert(0, envelope);
        if (_history.Count > 20)
        {
            _history.RemoveRange(20, _history.Count - 20);
        }

        using var connection = OpenConnection();
        connection.Execute(
            """
            INSERT INTO ActionResultHistory (
                ResultId,
                Action,
                Source,
                GeneratedAtLocalTicks,
                RowsJson,
                RelatedOperationId,
                InsertedUtcTicks
            )
            VALUES (
                @ResultId,
                @Action,
                @Source,
                @GeneratedAtLocalTicks,
                @RowsJson,
                @RelatedOperationId,
                @InsertedUtcTicks
            );
            """,
            new
            {
                ResultId = Guid.NewGuid().ToString("N"),
                Action = (int)envelope.Action,
                envelope.Source,
                GeneratedAtLocalTicks = envelope.GeneratedAtLocal.Ticks,
                RowsJson = JsonSerializer.Serialize(envelope.Rows, JsonOptions),
                RelatedOperationId = relatedOperationId,
                InsertedUtcTicks = DateTime.UtcNow.Ticks
            });

        connection.Execute(
            """
            DELETE FROM ActionResultHistory
            WHERE ResultId IN (
                SELECT ResultId
                FROM ActionResultHistory
                ORDER BY InsertedUtcTicks DESC
                LIMIT -1 OFFSET 20
            );
            """);

        Raise(nameof(Latest));
        Raise(nameof(History));
        return Task.CompletedTask;
    }

    public void Save(ActionResultEnvelope envelope)
    {
        SaveAsync(envelope).GetAwaiter().GetResult();
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _latest = null;
        _history.Clear();
        using var connection = OpenConnection();
        connection.Execute("DELETE FROM ActionResultHistory;");
        Raise(nameof(Latest));
        Raise(nameof(History));
        return Task.CompletedTask;
    }

    public void Clear()
    {
        ClearAsync().GetAwaiter().GetResult();
    }

    private void Raise(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private System.Data.IDbConnection OpenConnection()
    {
        var connection = _database.OpenConnection();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS ActionResultHistory (
                ResultId TEXT PRIMARY KEY,
                Action INTEGER NOT NULL,
                Source TEXT NOT NULL,
                GeneratedAtLocalTicks INTEGER NOT NULL,
                RowsJson TEXT NOT NULL,
                RelatedOperationId TEXT NULL,
                InsertedUtcTicks INTEGER NOT NULL
            );
            """);
        return connection;
    }

    private sealed class ActionResultHistoryRow
    {
        public string ResultId { get; set; } = string.Empty;
        public int Action { get; set; }
        public string Source { get; set; } = string.Empty;
        public long GeneratedAtLocalTicks { get; set; }
        public string RowsJson { get; set; } = "[]";
        public string? RelatedOperationId { get; set; }
        public long InsertedUtcTicks { get; set; }
    }
}
