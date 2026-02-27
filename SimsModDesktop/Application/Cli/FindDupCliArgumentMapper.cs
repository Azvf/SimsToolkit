using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Cli;

public sealed class FindDupCliArgumentMapper : IActionCliArgumentMapper
{
    public SimsAction Action => SimsAction.FindDuplicates;
    public string CliActionName => "finddup";

    public void Map(ISimsExecutionInput input, List<string> args)
    {
        if (input is not FindDupInput findDup)
        {
            throw new ArgumentException("FindDuplicates mapper received incompatible input.", nameof(input));
        }

        CliArgumentWriter.AddString(args, "-FindDupRootPath", findDup.FindDupRootPath);
        CliArgumentWriter.AddString(args, "-FindDupOutputCsv", findDup.FindDupOutputCsv);
        CliArgumentWriter.AddSwitch(args, "-FindDupRecurse", findDup.FindDupRecurse);
        CliArgumentWriter.AddSwitch(args, "-FindDupCleanup", findDup.FindDupCleanup);
        CliArgumentWriter.AddSharedFileOps(args, findDup.Shared);
    }
}
