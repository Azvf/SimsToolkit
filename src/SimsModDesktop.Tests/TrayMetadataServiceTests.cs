using System.Reflection;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class TrayMetadataServiceTests
{
    [Fact]
    public void BuildExtractionScript_SerializesListContentsInsteadOfListObject()
    {
        var method = typeof(TrayMetadataService).GetMethod(
            "BuildExtractionScript",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var script = (string?)method!.Invoke(
            null,
            new object[]
            {
                @"D:\Sims Mods\Tools\S4TI_250831",
                @"D:\Temp\input.json",
                @"D:\Temp\output.json"
            });

        Assert.NotNull(script);
        Assert.Contains("ConvertTo-Json -InputObject @($results.ToArray()) -Depth 8 -Compress", script, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath $outputPath -Value $json -Encoding UTF8", script, StringComparison.Ordinal);
        Assert.DoesNotContain("(@($results) | ConvertTo-Json", script, StringComparison.Ordinal);
    }
}
