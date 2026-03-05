namespace SimsModDesktop.PackageCore.Performance;

public sealed class PerformanceAdaptiveThrottle
{
    private readonly int _targetWorkers;
    private readonly int _minWorkers;
    private readonly TimeSpan _window;
    private readonly Queue<double> _previousWindowThroughputs = new();
    private DateTime _lastSampleUtc;
    private long _lastCompletedCount;
    private int _lowThroughputStreak;
    private int _recoveryStreak;
    private DateTime? _highMemorySinceUtc;

    public PerformanceAdaptiveThrottle(
        int targetWorkers,
        int minWorkers,
        DateTime startedAtUtc,
        TimeSpan? window = null)
    {
        _targetWorkers = Math.Max(1, targetWorkers);
        _minWorkers = Math.Max(1, Math.Min(minWorkers, _targetWorkers));
        _lastSampleUtc = startedAtUtc;
        _window = window.GetValueOrDefault(TimeSpan.FromSeconds(5));
        CurrentWorkers = _targetWorkers;
    }

    public int CurrentWorkers { get; private set; }

    public PerformanceAdaptiveThrottleDecision Update(
        long totalCompletedCount,
        DateTime nowUtc,
        long workingSetBytes,
        long baselineWorkingSetBytes)
    {
        if (nowUtc <= _lastSampleUtc || nowUtc - _lastSampleUtc < _window)
        {
            return PerformanceAdaptiveThrottleDecision.NoChange(CurrentWorkers);
        }

        var elapsedSeconds = Math.Max((nowUtc - _lastSampleUtc).TotalSeconds, 0.001d);
        var deltaCompleted = Math.Max(0, totalCompletedCount - _lastCompletedCount);
        var throughput = deltaCompleted / elapsedSeconds;
        _lastSampleUtc = nowUtc;
        _lastCompletedCount = totalCompletedCount;

        var previousAverage = _previousWindowThroughputs.Count == 0
            ? throughput
            : _previousWindowThroughputs.Average();
        if (_previousWindowThroughputs.Count == 6)
        {
            _previousWindowThroughputs.Dequeue();
        }

        _previousWindowThroughputs.Enqueue(throughput);

        if (baselineWorkingSetBytes > 0 && workingSetBytes > baselineWorkingSetBytes * 1.2d)
        {
            _highMemorySinceUtc ??= nowUtc;
            if (nowUtc - _highMemorySinceUtc >= TimeSpan.FromSeconds(10))
            {
                return Downscale("memory-pressure");
            }
        }
        else
        {
            _highMemorySinceUtc = null;
        }

        if (previousAverage > 0 && throughput < previousAverage * 0.85d)
        {
            _lowThroughputStreak++;
        }
        else
        {
            _lowThroughputStreak = 0;
        }

        if (previousAverage > 0 && throughput >= previousAverage * 0.95d)
        {
            _recoveryStreak++;
        }
        else
        {
            _recoveryStreak = 0;
        }

        if (_lowThroughputStreak >= 3)
        {
            return Downscale("throughput-drop");
        }

        if (_recoveryStreak >= 4 && CurrentWorkers < _targetWorkers)
        {
            _recoveryStreak = 0;
            _lowThroughputStreak = 0;
            CurrentWorkers++;
            return new PerformanceAdaptiveThrottleDecision(
                CurrentWorkers,
                Changed: true,
                Reason: "throughput-recovery");
        }

        return PerformanceAdaptiveThrottleDecision.NoChange(CurrentWorkers);
    }

    private PerformanceAdaptiveThrottleDecision Downscale(string reason)
    {
        _lowThroughputStreak = 0;
        _recoveryStreak = 0;
        _highMemorySinceUtc = null;

        var reduced = Math.Max(_minWorkers, (int)Math.Floor(CurrentWorkers * 0.75d));
        if (reduced >= CurrentWorkers)
        {
            return PerformanceAdaptiveThrottleDecision.NoChange(CurrentWorkers);
        }

        CurrentWorkers = reduced;
        return new PerformanceAdaptiveThrottleDecision(
            CurrentWorkers,
            Changed: true,
            Reason: reason);
    }
}

public readonly record struct PerformanceAdaptiveThrottleDecision(
    int RecommendedWorkers,
    bool Changed,
    string Reason)
{
    public static PerformanceAdaptiveThrottleDecision NoChange(int workers)
    {
        return new PerformanceAdaptiveThrottleDecision(
            workers,
            Changed: false,
            Reason: string.Empty);
    }
}
