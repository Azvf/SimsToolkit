using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Cli;

public sealed class SimsCliArgumentBuilder : ISimsCliArgumentBuilder
{
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
        args.Add("-Action");
        args.Add(ToCliAction(input.Action));

        switch (input)
        {
            case OrganizeInput organize:
                AddStringArgument(args, "-SourceDir", organize.SourceDir);
                AddStringArgument(args, "-ZipNamePattern", organize.ZipNamePattern);
                AddStringArgument(args, "-ModsRoot", organize.ModsRoot);
                AddStringArgument(args, "-UnifiedModsFolder", organize.UnifiedModsFolder);
                AddStringArgument(args, "-TrayRoot", organize.TrayRoot);
                AddSwitchArgument(args, "-KeepZip", organize.KeepZip);
                break;
            case FlattenInput flatten:
                AddStringArgument(args, "-FlattenRootPath", flatten.FlattenRootPath);
                AddSwitchArgument(args, "-FlattenToRoot", flatten.FlattenToRoot);
                AddSharedFileOpsArguments(args, flatten.Shared);
                break;
            case NormalizeInput normalize:
                AddStringArgument(args, "-NormalizeRootPath", normalize.NormalizeRootPath);
                break;
            case MergeInput merge:
                AddStringArrayArgument(args, "-MergeSourcePaths", merge.MergeSourcePaths);
                AddStringArgument(args, "-MergeTargetPath", merge.MergeTargetPath);
                AddSharedFileOpsArguments(args, merge.Shared);
                break;
            case FindDupInput findDup:
                AddStringArgument(args, "-FindDupRootPath", findDup.FindDupRootPath);
                AddStringArgument(args, "-FindDupOutputCsv", findDup.FindDupOutputCsv);
                AddSwitchArgument(args, "-FindDupRecurse", findDup.FindDupRecurse);
                AddSwitchArgument(args, "-FindDupCleanup", findDup.FindDupCleanup);
                AddSharedFileOpsArguments(args, findDup.Shared);
                break;
            case TrayDependenciesInput trayDependencies:
                AddStringArgument(args, "-TrayPath", trayDependencies.TrayPath);
                AddStringArgument(args, "-ModsPath", trayDependencies.ModsPath);
                AddStringArgument(args, "-TrayItemKey", trayDependencies.TrayItemKey);
                AddStringArgument(args, "-AnalysisMode", trayDependencies.AnalysisMode);
                AddStringArgument(args, "-S4tiPath", trayDependencies.S4tiPath);
                AddIntArgument(args, "-MinMatchCount", trayDependencies.MinMatchCount);
                AddIntArgument(args, "-TopN", trayDependencies.TopN);
                AddIntArgument(args, "-MaxPackageCount", trayDependencies.MaxPackageCount);
                AddStringArgument(args, "-OutputCsv", trayDependencies.OutputCsv);
                AddSwitchArgument(args, "-ExportUnusedPackages", trayDependencies.ExportUnusedPackages);
                AddStringArgument(args, "-UnusedOutputCsv", trayDependencies.UnusedOutputCsv);
                AddSwitchArgument(args, "-ExportMatchedPackages", trayDependencies.ExportMatchedPackages);
                AddStringArgument(args, "-ExportTargetPath", trayDependencies.ExportTargetPath);
                AddStringArgument(args, "-ExportMinConfidence", trayDependencies.ExportMinConfidence);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input.Action, "Unsupported action input.");
        }

        AddSwitchArgument(args, "-WhatIf", input.WhatIf);

        return new SimsProcessCommand
        {
            Arguments = args
        };
    }

    private static void AddSharedFileOpsArguments(List<string> args, SharedFileOpsInput shared)
    {
        AddSwitchArgument(args, "-SkipPruneEmptyDirs", shared.SkipPruneEmptyDirs);
        AddSwitchArgument(args, "-ModFilesOnly", shared.ModFilesOnly);
        AddStringArrayArgument(args, "-ModExtensions", shared.ModExtensions);
        AddSwitchArgument(args, "-VerifyContentOnNameConflict", shared.VerifyContentOnNameConflict);
        AddIntArgument(args, "-PrefixHashBytes", shared.PrefixHashBytes);
        AddIntArgument(args, "-HashWorkerCount", shared.HashWorkerCount);
    }

    private static string ToCliAction(SimsAction action)
    {
        return action switch
        {
            SimsAction.Organize => "organize",
            SimsAction.Flatten => "flatten",
            SimsAction.Normalize => "normalize",
            SimsAction.Merge => "merge",
            SimsAction.FindDuplicates => "finddup",
            SimsAction.TrayDependencies => "trayprobe",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported action.")
        };
    }

    private static void AddStringArgument(List<string> args, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        args.Add(name);
        args.Add(value.Trim());
    }

    private static void AddStringArrayArgument(List<string> args, string name, IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        args.Add(name);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            args.Add(value.Trim());
        }
    }

    private static void AddSwitchArgument(List<string> args, string name, bool enabled)
    {
        if (enabled)
        {
            args.Add(name);
        }
    }

    private static void AddIntArgument(List<string> args, string name, int? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        args.Add(name);
        args.Add(value.Value.ToString());
    }
}
