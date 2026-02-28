using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Cli;

public sealed class TrayDependenciesCliArgumentMapper : IActionCliArgumentMapper
{
    public SimsAction Action => SimsAction.TrayDependencies;
    public string CliActionName => "trayprobe";

    public void Map(ISimsExecutionInput input, List<string> args)
    {
        if (input is not TrayDependenciesInput trayDependencies)
        {
            throw new ArgumentException("TrayDependencies mapper received incompatible input.", nameof(input));
        }

        CliArgumentWriter.AddString(args, "-TrayPath", trayDependencies.TrayPath);
        CliArgumentWriter.AddString(args, "-ModsPath", trayDependencies.ModsPath);
        CliArgumentWriter.AddString(args, "-TrayItemKey", trayDependencies.TrayItemKey);
        CliArgumentWriter.AddString(args, "-AnalysisMode", "StrictS4TI");
        CliArgumentWriter.AddString(args, "-S4tiPath", trayDependencies.S4tiPath);
        CliArgumentWriter.AddInt(args, "-MinMatchCount", trayDependencies.MinMatchCount);
        CliArgumentWriter.AddInt(args, "-TopN", trayDependencies.TopN);
        CliArgumentWriter.AddInt(args, "-MaxPackageCount", trayDependencies.MaxPackageCount);
        CliArgumentWriter.AddString(args, "-OutputCsv", trayDependencies.OutputCsv);
        CliArgumentWriter.AddSwitch(args, "-ExportUnusedPackages", trayDependencies.ExportUnusedPackages);
        CliArgumentWriter.AddString(args, "-UnusedOutputCsv", trayDependencies.UnusedOutputCsv);
        CliArgumentWriter.AddSwitch(args, "-ExportMatchedPackages", trayDependencies.ExportMatchedPackages);
        CliArgumentWriter.AddString(args, "-ExportTargetPath", trayDependencies.ExportTargetPath);
        CliArgumentWriter.AddString(args, "-ExportMinConfidence", trayDependencies.ExportMinConfidence);
    }
}
