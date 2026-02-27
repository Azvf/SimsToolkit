namespace SimsModDesktop.Models;

public sealed class SimsExecutionResult
{
    public required int ExitCode { get; init; }
    public required string Executable { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
}
