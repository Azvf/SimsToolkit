using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Cli;

public interface IActionCliArgumentMapper
{
    SimsAction Action { get; }
    string CliActionName { get; }
    void Map(ISimsExecutionInput input, List<string> args);
}
