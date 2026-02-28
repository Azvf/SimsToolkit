using SimsModDesktop.Application.Results;
using SimsModDesktop.Models;

namespace SimsModDesktop.Tests;

public sealed class ExecutionOutputParsersTests
{
    [Fact]
    public void TrayPreviewParser_MapsRowsFromPreviewItems()
    {
        var parser = new TrayPreviewOutputParser();
        var context = new ExecutionOutputParseContext
        {
            Action = SimsAction.TrayPreview,
            TrayPreviewItems =
            [
                new SimsTrayPreviewItem
                {
                    TrayItemKey = "0x1",
                    PresetType = "Lot",
                    FileCount = 3,
                    TotalBytes = 1024,
                    TotalMB = 1,
                    LatestWriteTimeLocal = DateTime.Now
                }
            ]
        };

        var ok = parser.TryParse(context, out var envelope, out var error);

        Assert.True(ok, error);
        Assert.Single(envelope.Rows);
        Assert.Equal("0x1", envelope.Rows[0].Name);
        Assert.Equal("Lot", envelope.Rows[0].Status);
    }

    [Fact]
    public void TrayDependenciesParser_ParsesCsvFromLog()
    {
        using var csv = new TempCsv(
            "PackagePath,Confidence,MatchInstanceCount,MatchRatePct,PackageSizeBytes",
            @"C:\mods\a.package,High,3,65,2048");

        var parser = new TrayDependenciesOutputParser();
        var context = new ExecutionOutputParseContext
        {
            Action = SimsAction.TrayDependencies,
            LogText = $"foo{Environment.NewLine}CSV: {csv.Path}"
        };

        var ok = parser.TryParse(context, out var envelope, out var error);

        Assert.True(ok, error);
        Assert.Single(envelope.Rows);
        Assert.Equal("Conflict", envelope.Rows[0].Status);
        Assert.Equal("High", envelope.Rows[0].Confidence);
    }

    [Fact]
    public void FindDupParser_MissingCsv_ReturnsFalse()
    {
        var parser = new FindDupOutputParser();
        var context = new ExecutionOutputParseContext
        {
            Action = SimsAction.FindDuplicates,
            LogText = "Exported to: C:\\missing.csv"
        };

        var ok = parser.TryParse(context, out _, out var error);

        Assert.False(ok);
        Assert.Contains("CSV", error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempCsv : IDisposable
    {
        public TempCsv(params string[] lines)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
            File.WriteAllLines(Path, lines);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
