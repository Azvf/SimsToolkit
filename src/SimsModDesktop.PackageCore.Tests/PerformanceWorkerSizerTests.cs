using SimsModDesktop.PackageCore.Performance;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class PerformanceWorkerSizerTests
{
    [Fact]
    public void ResolveWriteBatchSize_ClampsRequestedValue()
    {
        Assert.Equal(64, PerformanceWorkerSizer.ResolveWriteBatchSize(8, defaultBatchSize: 512, min: 64, max: 4096));
        Assert.Equal(4096, PerformanceWorkerSizer.ResolveWriteBatchSize(99999, defaultBatchSize: 512, min: 64, max: 4096));
    }

    [Fact]
    public void ResolveHashWorkers_UsesAggressiveDefault()
    {
        Assert.Equal(12, PerformanceWorkerSizer.ResolveHashWorkers());
        Assert.Equal(64, PerformanceWorkerSizer.ResolveHashWorkers(128));
    }
}
