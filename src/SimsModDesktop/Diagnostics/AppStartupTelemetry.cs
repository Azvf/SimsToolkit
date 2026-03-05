using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SimsModDesktop.Diagnostics;

internal static class AppStartupTelemetry
{
    private static readonly object Sync = new();
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    private static readonly StartupLogBuffer Buffer = new();
    private static bool _hasMarkedFirstContentVisible;
    private static ILogger? _logger;

    public static void ResetForMainEntry()
    {
        lock (Sync)
        {
            Stopwatch.Restart();
            _hasMarkedFirstContentVisible = false;
            _logger = null;
            Buffer.Drain();
        }
    }

    public static void BindLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        IReadOnlyList<StartupBufferedLogEntry> bufferedEntries;
        lock (Sync)
        {
            _logger = logger;
            bufferedEntries = Buffer.Drain();
        }

        foreach (var entry in bufferedEntries)
        {
            _logger.LogInformation(
                "{Event} status={Status} milestone={Milestone} elapsedMs={ElapsedMs}",
                entry.Event,
                entry.Status,
                entry.Milestone,
                entry.ElapsedMs);
        }
    }

    public static void RecordMilestone(string name, ILogger? logger = null)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name) ? "unknown" : name.Trim();
        Write(logger, StartupLogEvents.StartupMilestone, "mark", normalizedName, null);
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

        Write(logger, StartupLogEvents.StartupFirstContentVisible, "done", "first_content_visible", null);
    }

    private static void Write(ILogger? logger, string eventName, string status, string milestone, Exception? exception)
    {
        var elapsedMs = Stopwatch.ElapsedMilliseconds;
        var activeLogger = logger ?? _logger;
        if (activeLogger is null)
        {
            Buffer.Add(eventName, status, milestone, elapsedMs);
        }

        if (activeLogger is not null)
        {
            if (exception is null)
            {
                activeLogger.LogInformation(
                    "{Event} status={Status} milestone={Milestone} elapsedMs={ElapsedMs}",
                    eventName,
                    status,
                    milestone,
                    elapsedMs);
                return;
            }

            activeLogger.LogError(
                exception,
                "{Event} status={Status} milestone={Milestone} elapsedMs={ElapsedMs}",
                eventName,
                status,
                milestone,
                elapsedMs);
            return;
        }

        var text = $"[{eventName}][{status}] milestone={milestone} elapsedMs={elapsedMs}";
        if (exception is null)
        {
            Trace.WriteLine(text);
            return;
        }

        Trace.WriteLine(text + " error=" + exception.Message);
    }
}
