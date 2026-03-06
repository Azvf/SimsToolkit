namespace SimsModDesktop.Presentation.Warmup;

internal enum WarmupSessionStateKind
{
    Scheduled,
    Running,
    Completed,
    Failed,
    Cancelled
}

internal sealed record WarmupSessionState
{
    public WarmupSessionStateKind Kind { get; init; }
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string StatusText { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public bool IsReusable { get; init; }
}
