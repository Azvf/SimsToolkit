using SimsModDesktop.Application.Results;
using SimsModDesktop.Models;

namespace SimsModDesktop.ViewModels.Inspector;

public sealed class TrayDependenciesInspectorPresenter : IInspectorPresenter
{
    public bool CanPresent(SimsAction action) => action == SimsAction.TrayDependencies;

    public IReadOnlyList<string> BuildDetails(ActionResultRow row)
    {
        return
        [
            $"Package: {row.Name}",
            $"Status: {row.Status}",
            $"Confidence: {row.Confidence}",
            row.DependencyInfo,
            row.RawSummary,
            $"Path: {row.PrimaryPath}"
        ];
    }
}
