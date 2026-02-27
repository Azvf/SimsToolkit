using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Cli;

public sealed class OrganizeCliArgumentMapper : IActionCliArgumentMapper
{
    public SimsAction Action => SimsAction.Organize;
    public string CliActionName => "organize";

    public void Map(ISimsExecutionInput input, List<string> args)
    {
        if (input is not OrganizeInput organize)
        {
            throw new ArgumentException("Organize mapper received incompatible input.", nameof(input));
        }

        CliArgumentWriter.AddString(args, "-SourceDir", organize.SourceDir);
        CliArgumentWriter.AddString(args, "-ZipNamePattern", organize.ZipNamePattern);
        CliArgumentWriter.AddString(args, "-ModsRoot", organize.ModsRoot);
        CliArgumentWriter.AddString(args, "-UnifiedModsFolder", organize.UnifiedModsFolder);
        CliArgumentWriter.AddString(args, "-TrayRoot", organize.TrayRoot);
        CliArgumentWriter.AddSwitch(args, "-KeepZip", organize.KeepZip);
    }
}
