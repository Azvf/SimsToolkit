using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Results;

public sealed class TrayPreviewOutputParser : IExecutionOutputParser
{
    public bool CanParse(SimsAction action) => action == SimsAction.TrayPreview;

    public bool TryParse(
        ExecutionOutputParseContext context,
        out ActionResultEnvelope envelope,
        out string error)
    {
        error = string.Empty;
        var rows = context.TrayPreviewItems
            .Select(item => new ActionResultRow
            {
                Name = string.IsNullOrWhiteSpace(item.ItemName) ? item.TrayItemKey : item.ItemName,
                Status = item.PresetType,
                SizeBytes = item.TotalBytes,
                UpdatedLocal = item.LatestWriteTimeLocal == DateTime.MinValue ? null : item.LatestWriteTimeLocal,
                Confidence = "n/a",
                Category = "TrayPreview",
                DependencyInfo = string.IsNullOrWhiteSpace(item.AuthorId)
                    ? item.ResourceTypes
                    : $"AuthorId={item.AuthorId}; {item.ResourceTypes}",
                RawSummary = string.IsNullOrWhiteSpace(item.FileListPreview)
                    ? item.TrayItemKey
                    : item.FileListPreview
            })
            .ToArray();

        envelope = new ActionResultEnvelope
        {
            Action = SimsAction.TrayPreview,
            Source = "tray-preview-cache",
            Rows = rows
        };

        return true;
    }
}
