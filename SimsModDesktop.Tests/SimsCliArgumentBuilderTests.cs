using SimsModDesktop.Application.Cli;
using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Tests;

public sealed class SimsCliArgumentBuilderTests
{
    [Fact]
    public void Build_MergeInput_MapsExpectedArgumentsInOrder()
    {
        var builder = new SimsCliArgumentBuilder();
        var input = new MergeInput
        {
            ScriptPath = "C:\\tools\\sims-mod-cli.ps1",
            WhatIf = true,
            MergeSourcePaths = new[] { "D:\\Mods\\A", "D:\\Mods\\B" },
            MergeTargetPath = "D:\\Mods\\Merged",
            Shared = new SharedFileOpsInput
            {
                SkipPruneEmptyDirs = true,
                ModFilesOnly = true,
                ModExtensions = new[] { ".package", ".ts4script" },
                VerifyContentOnNameConflict = true,
                PrefixHashBytes = 102400,
                HashWorkerCount = 8
            }
        };

        var command = builder.Build(input);

        Assert.Contains("-Action", command.Arguments);
        Assert.Contains("merge", command.Arguments);
        Assert.Contains("-MergeSourcePaths", command.Arguments);
        Assert.Contains("D:\\Mods\\A", command.Arguments);
        Assert.Contains("D:\\Mods\\B", command.Arguments);
        Assert.Contains("-MergeTargetPath", command.Arguments);
        Assert.Contains("D:\\Mods\\Merged", command.Arguments);
        Assert.Contains("-SkipPruneEmptyDirs", command.Arguments);
        Assert.Contains("-ModFilesOnly", command.Arguments);
        Assert.Contains("-VerifyContentOnNameConflict", command.Arguments);
        Assert.Contains("-PrefixHashBytes", command.Arguments);
        Assert.Contains("102400", command.Arguments);
        Assert.Contains("-HashWorkerCount", command.Arguments);
        Assert.Contains("8", command.Arguments);
        Assert.Contains("-WhatIf", command.Arguments);
    }
}
