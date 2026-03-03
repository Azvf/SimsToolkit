using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Results;

public sealed record ActionResultEnvelope
{
    public required SimsAction Action { get; init; }
    public required string Source { get; init; }
    public DateTime GeneratedAtLocal { get; init; } = DateTime.Now;
    public IReadOnlyList<ActionResultRow> Rows { get; init; } = Array.Empty<ActionResultRow>();
}
