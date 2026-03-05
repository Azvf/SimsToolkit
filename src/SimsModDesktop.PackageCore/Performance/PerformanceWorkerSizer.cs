namespace SimsModDesktop.PackageCore.Performance;

public static class PerformanceWorkerSizer
{
    public static int ResolveModsFastWorkers(int? requestedWorkers = null)
    {
        return Resolve(requestedWorkers, CpuCount(), min: 4, max: 16);
    }

    public static int ResolveModsDeepWorkers(int? requestedWorkers = null)
    {
        return Resolve(requestedWorkers, (int)Math.Ceiling(CpuCount() / 2d), min: 3, max: 10);
    }

    public static int ResolveTrayCacheParseWorkers(int? requestedWorkers = null)
    {
        return Resolve(requestedWorkers, CpuCount(), min: 4, max: 16);
    }

    public static int ResolveTrayMetadataWorkers(int? requestedWorkers = null)
    {
        return Resolve(requestedWorkers, (int)Math.Ceiling(CpuCount() / 2d), min: 4, max: 12);
    }

    public static int ResolveOrganizeWorkers(int? requestedWorkers = null)
    {
        return Resolve(requestedWorkers, (int)Math.Ceiling(CpuCount() / 2d), min: 4, max: 8);
    }

    public static int ResolveSavePreviewWorkers(int? requestedWorkers = null)
    {
        return Resolve(requestedWorkers, (int)Math.Ceiling(CpuCount() / 2d), min: 4, max: 12);
    }

    public static int ResolveHashWorkers(int? requestedWorkers = null)
    {
        return Resolve(requestedWorkers, defaultWorkers: 12, min: 1, max: 64);
    }

    public static int ResolveWriteBatchSize(int? requestedBatchSize = null, int defaultBatchSize = 512, int min = 32, int max = 4096)
    {
        var value = requestedBatchSize.GetValueOrDefault(defaultBatchSize);
        return Math.Clamp(value, min, max);
    }

    private static int Resolve(int? requestedWorkers, int defaultWorkers, int min, int max)
    {
        if (requestedWorkers.HasValue)
        {
            return Math.Clamp(requestedWorkers.Value, min, max);
        }

        return Math.Clamp(defaultWorkers, min, max);
    }

    private static int CpuCount()
    {
        return Math.Max(1, Environment.ProcessorCount);
    }
}
