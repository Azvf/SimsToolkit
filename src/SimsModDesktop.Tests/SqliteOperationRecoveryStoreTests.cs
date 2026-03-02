using System.Text.Json;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Models;

namespace SimsModDesktop.Tests;

public sealed class SqliteOperationRecoveryStoreTests
{
    [Fact]
    public async Task CreatePendingAndComplete_PersistsAndUpdatesRecoverableState()
    {
        using var cacheDir = new TempDirectory("recovery-store");
        var store = new SqliteOperationRecoveryStore(cacheDir.Path);

        var operationId = await store.CreatePendingAsync(new RecoverableOperationPayload
        {
            Workspace = AppWorkspace.Toolkit,
            Action = SimsAction.TrayPreview,
            DisplayTitle = "Resume previous tray preview load",
            PayloadKind = "TrayPreview",
            PayloadJson = JsonSerializer.Serialize(new { TrayPath = "D:\\Tray" }),
            LaunchSource = RecoverableOperationLaunchSource.TrayPreview
        });

        var recoverable = await store.GetRecoverableAsync();
        var record = Assert.Single(recoverable);
        Assert.Equal(operationId, record.OperationId);
        Assert.Equal(OperationRecoveryStatus.Pending, record.Status);
        Assert.True(File.Exists(Path.Combine(cacheDir.Path, "app-cache.db")));

        await store.MarkStartedAsync(operationId);
        recoverable = await store.GetRecoverableAsync();
        record = Assert.Single(recoverable);
        Assert.Equal(OperationRecoveryStatus.Running, record.Status);
        Assert.NotNull(record.StartedUtc);
        Assert.NotNull(record.LastHeartbeatUtc);

        await store.MarkCompletedAsync(
            operationId,
            new RecoverableOperationCompletion
            {
                Status = OperationRecoveryStatus.Succeeded,
                ResultSummaryJson = "{\"ok\":true}"
            });

        recoverable = await store.GetRecoverableAsync();
        Assert.Empty(recoverable);
    }

    [Fact]
    public async Task AbandonAndClear_RemoveRecordsFromRecoverableList()
    {
        using var cacheDir = new TempDirectory("recovery-store-clear");
        var store = new SqliteOperationRecoveryStore(cacheDir.Path);

        var abandonedId = await store.CreatePendingAsync(CreatePayload("one"));
        var clearedId = await store.CreatePendingAsync(CreatePayload("two"));

        await store.MarkAbandonedAsync(abandonedId);
        await store.ClearAsync(clearedId);

        var recoverable = await store.GetRecoverableAsync();
        Assert.Empty(recoverable);
    }

    private static RecoverableOperationPayload CreatePayload(string suffix)
    {
        return new RecoverableOperationPayload
        {
            Workspace = AppWorkspace.Toolkit,
            Action = SimsAction.Organize,
            DisplayTitle = "Resume previous organize task",
            PayloadKind = "ToolkitCli",
            PayloadJson = JsonSerializer.Serialize(new { InputType = suffix }),
            LaunchSource = RecoverableOperationLaunchSource.Toolkit
        };
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
