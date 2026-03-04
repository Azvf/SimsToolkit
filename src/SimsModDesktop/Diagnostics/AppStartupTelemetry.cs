using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SimsModDesktop.Diagnostics;

internal static class AppStartupTelemetry
{
    private static readonly object Sync = new();
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    private static bool _hasMarkedFirstContentVisible;

    public static void ResetForMainEntry()
    {
        lock (Sync)
        {
            Stopwatch.Restart();
            _hasMarkedFirstContentVisible = false;
        }
    }

    public static void RecordMilestone(string name, ILogger? logger = null)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name) ? "unknown" : name.Trim();
        Write(logger, "milestone", normalizedName, null);
    }

    public static void MarkFirstContentVisible(ILogger? logger = null)
    {
        lock (Sync)
        {
            if (_hasMarkedFirstContentVisible)
            {
                return;
            }

            _hasMarkedFirstContentVisible = true;
        }

        Write(logger, "done", "first_content_visible", null);
    }

    private static void Write(ILogger? logger, string status, string milestone, Exception? exception)
    {
        var elapsedMs = Stopwatch.ElapsedMilliseconds;
        if (logger is not null)
        {
            if (exception is null)
            {
                logger.LogInformation(
                    "Startup {Status} milestone {Milestone} elapsedMs={ElapsedMs}",
                    status,
                    milestone,
                    elapsedMs);
                return;
            }

            logger.LogError(
                exception,
                "Startup {Status} milestone {Milestone} elapsedMs={ElapsedMs}",
                status,
                milestone,
                elapsedMs);
            return;
        }

        var text = $"[startup][{status}] milestone={milestone} elapsedMs={elapsedMs}";
        if (exception is null)
        {
            Trace.WriteLine(text);
            return;
        }

        Trace.WriteLine(text + " error=" + exception.Message);
    }
}
