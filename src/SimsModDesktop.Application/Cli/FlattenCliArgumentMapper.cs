using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Cli;

public sealed class FlattenCliArgumentMapper : IActionCliArgumentMapper
{
    public SimsAction Action => SimsAction.Flatten;
    public string CliActionName => "flatten";

    public void Map(ISimsExecutionInput input, List<string> args)
    {
        if (input is not FlattenInput flatten)
        {
            throw new ArgumentException("Flatten mapper received incompatible input.", nameof(input));
        }

        CliArgumentWriter.AddString(args, "-FlattenRootPath", flatten.FlattenRootPath);
        CliArgumentWriter.AddSwitch(args, "-FlattenToRoot", flatten.FlattenToRoot);
        CliArgumentWriter.AddSharedFileOps(args, flatten.Shared);
    }
}
