
namespace SimsModDesktop.Application.Recovery;

public enum OperationRecoveryStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Abandoned,
    Cleared
}

public enum RecoveryPromptAction
{
    Resume,
    Abandon,
    Clear
}

public enum RecoverableOperationLaunchSource
{
    Toolkit,
    TrayDependencies,
    TrayPreview
}

public sealed record RecoverableOperationPayload
{
    public string OperationId { get; init; } = string.Empty;
    public AppWorkspace Workspace { get; init; }
    public SimsAction Action { get; init; }
    public string DisplayTitle { get; init; } = string.Empty;
    public string PayloadKind { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = "{}";
    public RecoverableOperationLaunchSource LaunchSource { get; init; }
}

public sealed record RecoverableOperationRecord
{
    public string OperationId { get; init; } = string.Empty;
    public AppWorkspace Workspace { get; init; }
    public SimsAction Action { get; init; }
    public OperationRecoveryStatus Status { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime? StartedUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public DateTime? LastHeartbeatUtc { get; init; }
    public int RecoveryVersion { get; init; }
    public RecoverableOperationLaunchSource LaunchSource { get; init; }
    public string DisplayTitle { get; init; } = string.Empty;
    public RecoverableOperationPayload Payload { get; init; } = new();
    public string? ResultSummaryJson { get; init; }
    public string? FailureMessage { get; init; }
}

public sealed record RecoverableOperationCompletion
{
    public OperationRecoveryStatus Status { get; init; }
    public string? ResultSummaryJson { get; init; }
    public string? FailureMessage { get; init; }
}

public sealed record RecoveryPromptDecision
{
    public string OperationId { get; init; } = string.Empty;
    public RecoveryPromptAction Action { get; init; }
}
