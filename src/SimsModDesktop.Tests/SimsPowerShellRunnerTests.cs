using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class SimsPowerShellRunnerTests
{
    [Fact]
    public void TryParseProgressLine_ValidPayload_ReturnsProgress()
    {
        var parsed = SimsPowerShellRunner.TryParseProgressLine(
            "##SIMS_PROGRESS##|scan|20|100|20|phase detail",
            out var progress);

        Assert.True(parsed);
        Assert.Equal("scan", progress.Stage);
        Assert.Equal(20, progress.Current);
        Assert.Equal(100, progress.Total);
        Assert.Equal(20, progress.Percent);
        Assert.Equal("phase detail", progress.Detail);
    }

    [Fact]
    public void TryParseProgressLine_InvalidPayload_ReturnsFalse()
    {
        var parsed = SimsPowerShellRunner.TryParseProgressLine("plain output", out _);
        Assert.False(parsed);
    }
}
