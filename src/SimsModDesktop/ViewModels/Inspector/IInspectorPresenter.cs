using SimsModDesktop.Application.Results;
using SimsModDesktop.Models;

namespace SimsModDesktop.ViewModels.Inspector;

public interface IInspectorPresenter
{
    bool CanPresent(SimsAction action);

    IReadOnlyList<string> BuildDetails(ActionResultRow row);
}
