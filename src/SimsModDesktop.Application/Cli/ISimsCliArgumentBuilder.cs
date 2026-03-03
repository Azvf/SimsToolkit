using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Cli;

public interface ISimsCliArgumentBuilder
{
    SimsProcessCommand Build(ISimsExecutionInput input);
}
