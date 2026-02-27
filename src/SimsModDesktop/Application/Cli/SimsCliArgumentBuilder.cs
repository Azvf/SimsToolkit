using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Cli;

public sealed class SimsCliArgumentBuilder : ISimsCliArgumentBuilder
{
    private readonly IReadOnlyDictionary<Models.SimsAction, IActionCliArgumentMapper> _mappers;

    public SimsCliArgumentBuilder(IEnumerable<IActionCliArgumentMapper> mappers)
    {
        _mappers = mappers.ToDictionary(mapper => mapper.Action);
    }

    public SimsProcessCommand Build(ISimsExecutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var args = new List<string>
        {
            "-NoLogo",
            "-NoProfile"
        };

        if (OperatingSystem.IsWindows())
        {
            args.Add("-ExecutionPolicy");
            args.Add("Bypass");
        }

        args.Add("-File");
        args.Add(input.ScriptPath);

        var mapper = GetMapper(input.Action);
        args.Add("-Action");
        args.Add(mapper.CliActionName);
        mapper.Map(input, args);

        CliArgumentWriter.AddSwitch(args, "-WhatIf", input.WhatIf);

        return new SimsProcessCommand
        {
            Arguments = args
        };
    }

    private IActionCliArgumentMapper GetMapper(Models.SimsAction action)
    {
        if (_mappers.TryGetValue(action, out var mapper))
        {
            return mapper;
        }

        throw new InvalidOperationException($"CLI mapper is not registered: {action}.");
    }
}
