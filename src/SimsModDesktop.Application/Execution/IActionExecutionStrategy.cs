using SimsModDesktop.Application.Cli;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Execution;

public interface IActionExecutionStrategy
{
    SimsAction Action { get; }
    bool TryValidate(ISimsExecutionInput input, out string error);
    SimsProcessCommand BuildCommand(ISimsExecutionInput input);
}
