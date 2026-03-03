using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Cli;

public sealed class NormalizeCliArgumentMapper : IActionCliArgumentMapper
{
    public SimsAction Action => SimsAction.Normalize;
    public string CliActionName => "normalize";

    public void Map(ISimsExecutionInput input, List<string> args)
    {
        if (input is not NormalizeInput normalize)
        {
            throw new ArgumentException("Normalize mapper received incompatible input.", nameof(input));
        }

        CliArgumentWriter.AddString(args, "-NormalizeRootPath", normalize.NormalizeRootPath);
    }
}
