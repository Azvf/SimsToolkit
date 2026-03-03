
namespace SimsModDesktop.Application.Results;

public sealed record ExecutionOutputParseContext
{
    public required SimsAction Action { get; init; }
    public string LogText { get; init; } = string.Empty;
    public IReadOnlyList<TrayPreviewResultRow> TrayPreviewItems { get; init; } = Array.Empty<TrayPreviewResultRow>();
}
