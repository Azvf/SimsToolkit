using System.Text.Json;
using Dapper;
using SimsModDesktop.Infrastructure.Persistence;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Recovery;

public sealed class SqliteOperationRecoveryStore : IOperationRecoveryStore
{
    private const int RecoveryVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppCacheDatabase _database;

    public SqliteOperationRecoveryStore()
        : this(new AppCacheDatabase())
    {
    }

    internal SqliteOperationRecoveryStore(string cacheRootPath)
        : this(new AppCacheDatabase(cacheRootPath))
    {
    }

    private SqliteOperationRecoveryStore(AppCacheDatabase database)
    {
        _database = database;
    }

    public Task<string> CreatePendingAsync(RecoverableOperationPayload payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ct.ThrowIfCancellationRequested();

        var operationId = string.IsNullOrWhiteSpace(payload.OperationId)
            ? Guid.NewGuid().ToString("N")
            : payload.OperationId;
        var createdUtc = DateTime.UtcNow;

        using var connection = OpenConnection();
        connection.Execute(
            """
            INSERT INTO OperationRecoveryRecords (
                OperationId,
                Action,
                Workspace,
                Status,
                CreatedUtcTicks,
                StartedUtcTicks,
                CompletedUtcTicks,
                LastHeartbeatUtcTicks,
                RecoveryVersion,
                LaunchSource,
                UserVisibleTitle,
                PayloadJson,
                ResultSummaryJson,
                FailureMessage
            )
            VALUES (
                @OperationId,
                @Action,
                @Workspace,
                @Status,
                @CreatedUtcTicks,
                NULL,
                NULL,
                NULL,
                @RecoveryVersion,
                @LaunchSource,
                @UserVisibleTitle,
                @PayloadJson,
                NULL,
                NULL
            );
            """,
            new
            {
                OperationId = operationId,
                Action = (int)payload.Action,
                Workspace = (int)payload.Workspace,
                Status = OperationRecoveryStatus.Pending.ToString(),
                CreatedUtcTicks = createdUtc.Ticks,
                RecoveryVersion,
                LaunchSource = payload.LaunchSource.ToString(),
                UserVisibleTitle = payload.DisplayTitle,
                PayloadJson = JsonSerializer.Serialize(payload with { OperationId = operationId }, JsonOptions)
            });

        return Task.FromResult(operationId);
    }

    public Task MarkStartedAsync(string operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        UpdateStatus(
            operationId,
            OperationRecoveryStatus.Running,
            startedUtcTicks: DateTime.UtcNow.Ticks,
            completedUtcTicks: null,
            lastHeartbeatUtcTicks: DateTime.UtcNow.Ticks,
            resultSummaryJson: null,
            failureMessage: null);
        return Task.CompletedTask;
    }

    public Task MarkHeartbeatAsync(string operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = OpenConnection();
        connection.Execute(
            """
            UPDATE OperationRecoveryRecords
            SET LastHeartbeatUtcTicks = @LastHeartbeatUtcTicks
            WHERE OperationId = @OperationId;
            """,
            new
            {
                OperationId = operationId,
                LastHeartbeatUtcTicks = DateTime.UtcNow.Ticks
            });
        return Task.CompletedTask;
    }

    public Task MarkCompletedAsync(string operationId, RecoverableOperationCompletion completion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        ct.ThrowIfCancellationRequested();
        UpdateStatus(
            operationId,
            completion.Status,
            startedUtcTicks: null,
            completedUtcTicks: DateTime.UtcNow.Ticks,
            lastHeartbeatUtcTicks: DateTime.UtcNow.Ticks,
            resultSummaryJson: completion.ResultSummaryJson,
            failureMessage: completion.FailureMessage);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecoverableOperationRecord>> GetRecoverableAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var connection = OpenConnection();
        var rows = connection.Query<OperationRecoveryRow>(
            """
            SELECT
                OperationId,
                Action,
                Workspace,
                Status,
                CreatedUtcTicks,
                StartedUtcTicks,
                CompletedUtcTicks,
                LastHeartbeatUtcTicks,
                RecoveryVersion,
                LaunchSource,
                UserVisibleTitle,
                PayloadJson,
                ResultSummaryJson,
                FailureMessage
            FROM OperationRecoveryRecords
            WHERE Status IN (@PendingStatus, @RunningStatus)
            ORDER BY CreatedUtcTicks DESC;
            """,
            new
            {
                PendingStatus = OperationRecoveryStatus.Pending.ToString(),
                RunningStatus = OperationRecoveryStatus.Running.ToString()
            });

        var records = rows
            .Select(ToRecord)
            .Where(record => record is not null)
            .Cast<RecoverableOperationRecord>()
            .ToArray();

        return Task.FromResult<IReadOnlyList<RecoverableOperationRecord>>(records);
    }

    public Task MarkAbandonedAsync(string operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        UpdateStatus(
            operationId,
            OperationRecoveryStatus.Abandoned,
            startedUtcTicks: null,
            completedUtcTicks: DateTime.UtcNow.Ticks,
            lastHeartbeatUtcTicks: DateTime.UtcNow.Ticks,
            resultSummaryJson: null,
            failureMessage: null);
        return Task.CompletedTask;
    }

    public Task ClearAsync(string operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        UpdateStatus(
            operationId,
            OperationRecoveryStatus.Cleared,
            startedUtcTicks: null,
            completedUtcTicks: DateTime.UtcNow.Ticks,
            lastHeartbeatUtcTicks: DateTime.UtcNow.Ticks,
            resultSummaryJson: null,
            failureMessage: null);
        return Task.CompletedTask;
    }

    private void UpdateStatus(
        string operationId,
        OperationRecoveryStatus status,
        long? startedUtcTicks,
        long? completedUtcTicks,
        long? lastHeartbeatUtcTicks,
        string? resultSummaryJson,
        string? failureMessage)
    {
        using var connection = OpenConnection();
        connection.Execute(
            """
            UPDATE OperationRecoveryRecords
            SET Status = @Status,
                StartedUtcTicks = COALESCE(@StartedUtcTicks, StartedUtcTicks),
                CompletedUtcTicks = @CompletedUtcTicks,
                LastHeartbeatUtcTicks = @LastHeartbeatUtcTicks,
                ResultSummaryJson = @ResultSummaryJson,
                FailureMessage = @FailureMessage
            WHERE OperationId = @OperationId;
            """,
            new
            {
                OperationId = operationId,
                Status = status.ToString(),
                StartedUtcTicks = startedUtcTicks,
                CompletedUtcTicks = completedUtcTicks,
                LastHeartbeatUtcTicks = lastHeartbeatUtcTicks,
                ResultSummaryJson = resultSummaryJson,
                FailureMessage = failureMessage
            });
    }

    private System.Data.IDbConnection OpenConnection()
    {
        var connection = _database.OpenConnection();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS OperationRecoveryRecords (
                OperationId TEXT PRIMARY KEY,
                Action INTEGER NOT NULL,
                Workspace INTEGER NOT NULL,
                Status TEXT NOT NULL,
                CreatedUtcTicks INTEGER NOT NULL,
                StartedUtcTicks INTEGER NULL,
                CompletedUtcTicks INTEGER NULL,
                LastHeartbeatUtcTicks INTEGER NULL,
                RecoveryVersion INTEGER NOT NULL,
                LaunchSource TEXT NOT NULL,
                UserVisibleTitle TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                ResultSummaryJson TEXT NULL,
                FailureMessage TEXT NULL
            );
            """);
        return connection;
    }

    private static RecoverableOperationRecord? ToRecord(OperationRecoveryRow row)
    {
        var payload = JsonSerializer.Deserialize<RecoverableOperationPayload>(row.PayloadJson, JsonOptions);
        if (payload is null)
        {
            return null;
        }

        return new RecoverableOperationRecord
        {
            OperationId = row.OperationId,
            Workspace = (AppWorkspace)row.Workspace,
            Action = (SimsAction)row.Action,
            Status = Enum.TryParse<OperationRecoveryStatus>(row.Status, ignoreCase: true, out var status)
                ? status
                : OperationRecoveryStatus.Pending,
            CreatedUtc = new DateTime(row.CreatedUtcTicks, DateTimeKind.Utc),
            StartedUtc = row.StartedUtcTicks is long startedTicks ? new DateTime(startedTicks, DateTimeKind.Utc) : null,
            CompletedUtc = row.CompletedUtcTicks is long completedTicks ? new DateTime(completedTicks, DateTimeKind.Utc) : null,
            LastHeartbeatUtc = row.LastHeartbeatUtcTicks is long heartbeatTicks ? new DateTime(heartbeatTicks, DateTimeKind.Utc) : null,
            RecoveryVersion = row.RecoveryVersion,
            LaunchSource = Enum.TryParse<RecoverableOperationLaunchSource>(row.LaunchSource, ignoreCase: true, out var launchSource)
                ? launchSource
                : RecoverableOperationLaunchSource.Toolkit,
            DisplayTitle = row.UserVisibleTitle,
            Payload = payload with { OperationId = row.OperationId },
            ResultSummaryJson = row.ResultSummaryJson,
            FailureMessage = row.FailureMessage
        };
    }

    private sealed class OperationRecoveryRow
    {
        public string OperationId { get; set; } = string.Empty;
        public int Action { get; set; }
        public int Workspace { get; set; }
        public string Status { get; set; } = string.Empty;
        public long CreatedUtcTicks { get; set; }
        public long? StartedUtcTicks { get; set; }
        public long? CompletedUtcTicks { get; set; }
        public long? LastHeartbeatUtcTicks { get; set; }
        public int RecoveryVersion { get; set; }
        public string LaunchSource { get; set; } = string.Empty;
        public string UserVisibleTitle { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = "{}";
        public string? ResultSummaryJson { get; set; }
        public string? FailureMessage { get; set; }
    }
}
