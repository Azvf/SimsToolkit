namespace SimsModDesktop.Application.Results;

public interface IExecutionOutputParserRegistry
{
    bool TryParse(
        ExecutionOutputParseContext context,
        out ActionResultEnvelope envelope,
        out string error);
}
