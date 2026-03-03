using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Results;

public interface IExecutionOutputParser
{
    bool CanParse(SimsAction action);

    bool TryParse(
        ExecutionOutputParseContext context,
        out ActionResultEnvelope envelope,
        out string error);
}
