using SimsModDesktop.Application.Results;
using SimsModDesktop.Models;

namespace SimsModDesktop.ViewModels.Inspector;

public sealed class FindDupInspectorPresenter : IInspectorPresenter
{
    public bool CanPresent(SimsAction action) => action == SimsAction.FindDuplicates;

    public IReadOnlyList<string> BuildDetails(ActionResultRow row)
    {
        return
        [
            $"File: {row.Name}",
            $"Status: {row.Status}",
            $"Hash: {row.Hash}",
            row.RawSummary,
            $"Path: {row.PrimaryPath}"
        ];
    }
}
