namespace SimsModDesktop.Application.Results;

public sealed class ExecutionOutputParserRegistry : IExecutionOutputParserRegistry
{
    private readonly IReadOnlyList<IExecutionOutputParser> _parsers;

    public ExecutionOutputParserRegistry(IEnumerable<IExecutionOutputParser> parsers)
    {
        _parsers = parsers.ToArray();
    }

    public bool TryParse(
        ExecutionOutputParseContext context,
        out ActionResultEnvelope envelope,
        out string error)
    {
        envelope = null!;
        error = string.Empty;

        foreach (var parser in _parsers)
        {
            if (!parser.CanParse(context.Action))
            {
                continue;
            }

            return parser.TryParse(context, out envelope, out error);
        }

        error = $"No parser registered for action: {context.Action}";
        return false;
    }
}
