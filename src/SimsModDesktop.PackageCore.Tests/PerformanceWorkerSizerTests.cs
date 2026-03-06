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

    [Fact]
    public void ResolveTrayExportCopyWorkers_UsesDefaultAndClamp()
    {
        var expectedDefault = Math.Max(1, Math.Min(12, (int)Math.Ceiling(Environment.ProcessorCount / 2d)));
        Assert.Equal(expectedDefault, PerformanceWorkerSizer.ResolveTrayExportCopyWorkers());
        Assert.Equal(1, PerformanceWorkerSizer.ResolveTrayExportCopyWorkers(0));
        Assert.Equal(12, PerformanceWorkerSizer.ResolveTrayExportCopyWorkers(99));
    }

    [Fact]
    public void ResolveTrayPreviewPageWorkers_UsesCpuBasedDefaultAndClamp()
    {
        var expected = Math.Clamp((int)Math.Ceiling(Environment.ProcessorCount / 2d), 2, 8);
        Assert.Equal(expected, PerformanceWorkerSizer.ResolveTrayPreviewPageWorkers());
        Assert.Equal(2, PerformanceWorkerSizer.ResolveTrayPreviewPageWorkers(1));
        Assert.Equal(8, PerformanceWorkerSizer.ResolveTrayPreviewPageWorkers(99));
    }
}
