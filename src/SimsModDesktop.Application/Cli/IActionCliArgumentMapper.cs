using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Cli;

public interface IActionCliArgumentMapper
{
    SimsAction Action { get; }
    string CliActionName { get; }
    void Map(ISimsExecutionInput input, List<string> args);
}
