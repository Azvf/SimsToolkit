using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Cli;

internal static class CliArgumentWriter
{
    public static void AddString(List<string> args, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        args.Add(name);
        args.Add(value.Trim());
    }

    public static void AddStringArray(List<string> args, string name, IReadOnlyList<string>? values)
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

    public static void AddSwitch(List<string> args, string name, bool enabled)
    {
        if (enabled)
        {
            args.Add(name);
        }
    }

    public static void AddInt(List<string> args, string name, int? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        args.Add(name);
        args.Add(value.Value.ToString());
    }

    public static void AddSharedFileOps(List<string> args, SharedFileOpsInput shared)
    {
        AddSwitch(args, "-SkipPruneEmptyDirs", shared.SkipPruneEmptyDirs);
        AddSwitch(args, "-ModFilesOnly", shared.ModFilesOnly);
        AddStringArray(args, "-ModExtensions", shared.ModExtensions);
        AddSwitch(args, "-VerifyContentOnNameConflict", shared.VerifyContentOnNameConflict);
        AddInt(args, "-PrefixHashBytes", shared.PrefixHashBytes);
        AddInt(args, "-HashWorkerCount", shared.HashWorkerCount);
    }
}
