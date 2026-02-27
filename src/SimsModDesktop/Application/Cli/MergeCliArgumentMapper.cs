using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Cli;

public sealed class MergeCliArgumentMapper : IActionCliArgumentMapper
{
    public SimsAction Action => SimsAction.Merge;
    public string CliActionName => "merge";

    public void Map(ISimsExecutionInput input, List<string> args)
    {
        if (input is not MergeInput merge)
        {
            throw new ArgumentException("Merge mapper received incompatible input.", nameof(input));
        }

        CliArgumentWriter.AddStringArray(args, "-MergeSourcePaths", merge.MergeSourcePaths);
        CliArgumentWriter.AddString(args, "-MergeTargetPath", merge.MergeTargetPath);
        CliArgumentWriter.AddSharedFileOps(args, merge.Shared);
    }
}
