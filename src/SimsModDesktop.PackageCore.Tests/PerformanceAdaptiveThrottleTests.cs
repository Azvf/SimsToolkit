using SimsModDesktop.PackageCore.Performance;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class PerformanceAdaptiveThrottleTests
{
    [Fact]
    public void Update_DownscalesOnSustainedThroughputDrop()
    {
        var now = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc);
        var throttle = new PerformanceAdaptiveThrottle(
            targetWorkers: 8,
            minWorkers: 4,
            startedAtUtc: now,
            window: TimeSpan.FromSeconds(5));

        throttle.Update(50, now.AddSeconds(5), workingSetBytes: 100, baselineWorkingSetBytes: 100);
        throttle.Update(55, now.AddSeconds(10), workingSetBytes: 100, baselineWorkingSetBytes: 100);
        throttle.Update(60, now.AddSeconds(15), workingSetBytes: 100, baselineWorkingSetBytes: 100);
        var decision = throttle.Update(65, now.AddSeconds(20), workingSetBytes: 100, baselineWorkingSetBytes: 100);

        Assert.True(decision.Changed);
        Assert.Equal(6, decision.RecommendedWorkers);
        Assert.Equal("throughput-drop", decision.Reason);
    }

    [Fact]
    public void Update_DownscalesOnSustainedMemoryPressure()
    {
        var now = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc);
        var throttle = new PerformanceAdaptiveThrottle(
            targetWorkers: 8,
            minWorkers: 4,
            startedAtUtc: now,
            window: TimeSpan.FromSeconds(5));

        throttle.Update(40, now.AddSeconds(5), workingSetBytes: 130, baselineWorkingSetBytes: 100);
        throttle.Update(80, now.AddSeconds(10), workingSetBytes: 130, baselineWorkingSetBytes: 100);
        var decision = throttle.Update(120, now.AddSeconds(15), workingSetBytes: 130, baselineWorkingSetBytes: 100);

        Assert.True(decision.Changed);
        Assert.Equal(6, decision.RecommendedWorkers);
        Assert.Equal("memory-pressure", decision.Reason);
    }
}
