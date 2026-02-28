using SimsModDesktop.Application.Results;
using SimsModDesktop.Models;

namespace SimsModDesktop.ViewModels.Inspector;

public sealed class TrayPreviewInspectorPresenter : IInspectorPresenter
{
    public bool CanPresent(SimsAction action) => action == SimsAction.TrayPreview;

    public IReadOnlyList<string> BuildDetails(ActionResultRow row)
    {
        return
        [
            $"Name: {row.Name}",
            $"Type: {row.Status}",
            $"Updated: {row.UpdatedLocal:yyyy-MM-dd HH:mm}",
            $"Resource Types: {row.DependencyInfo}",
            $"Files: {row.RawSummary}"
        ];
    }
}
