using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SimsModDesktop.Presentation.Diagnostics;

internal sealed class PerformanceLogScope : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _operation;
    private readonly Action<string>? _appendUiLog;
    private readonly Stopwatch _stopwatch;
    private bool _completed;

    private PerformanceLogScope(
        ILogger logger,
        string operation,
        Action<string>? appendUiLog,
        IReadOnlyList<(string Key, object? Value)> tags)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operation = string.IsNullOrWhiteSpace(operation) ? "unknown" : operation.Trim();
        _appendUiLog = appendUiLog;
        _stopwatch = Stopwatch.StartNew();

        Write("start", null, tags);
    }

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

    public static PerformanceLogScope Begin(
        ILogger logger,
        string operation,
        Action<string>? appendUiLog = null,
        params (string Key, object? Value)[] tags)
    {
        return new PerformanceLogScope(logger, operation, appendUiLog, tags ?? []);
    }

    public void Success(string? message = null, params (string Key, object? Value)[] tags)
    {
        Complete("done", null, message, tags);
    }

    public void Fail(Exception ex, string? message = null, params (string Key, object? Value)[] tags)
    {
        ArgumentNullException.ThrowIfNull(ex);
        Complete("fail", ex, message, tags);
    }

    public void Cancel(string? message = null, params (string Key, object? Value)[] tags)
    {
        Complete("cancel", null, message, tags);
    }

    public void Skip(string? message = null, params (string Key, object? Value)[] tags)
    {
        Complete("skip", null, message, tags);
    }

    public void Mark(string milestone, params (string Key, object? Value)[] tags)
    {
        var normalizedMilestone = string.IsNullOrWhiteSpace(milestone) ? "milestone" : milestone.Trim();
        var combined = CombineTags(tags, ("milestone", normalizedMilestone));
        Write("mark", null, combined);
    }

    private void Complete(
        string status,
        Exception? exception,
        string? message,
        IReadOnlyList<(string Key, object? Value)> tags)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _stopwatch.Stop();
        var combined = string.IsNullOrWhiteSpace(message)
            ? tags
            : CombineTags(tags, ("message", message));
        Write(status, exception, combined);
    }

    private void Write(
        string status,
        Exception? exception,
        IReadOnlyList<(string Key, object? Value)> tags)
    {
        var context = FormatTags(tags);
        if (exception is null)
        {
            if (string.Equals(status, "cancel", StringComparison.Ordinal) ||
                string.Equals(status, "skip", StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Timing {Status} operation {Operation} elapsedMs={ElapsedMs} {Context}",
                    status,
                    _operation,
                    _stopwatch.ElapsedMilliseconds,
                    context);
            }
            else
            {
                _logger.LogInformation(
                    "Timing {Status} operation {Operation} elapsedMs={ElapsedMs} {Context}",
                    status,
                    _operation,
                    _stopwatch.ElapsedMilliseconds,
                    context);
            }
        }
        else
        {
            _logger.LogError(
                exception,
                "Timing {Status} operation {Operation} elapsedMs={ElapsedMs} {Context}",
                status,
                _operation,
                _stopwatch.ElapsedMilliseconds,
                context);
        }

        if (_appendUiLog is null)
        {
            return;
        }

        var text = $"[timing][{status}] operation={_operation}";
        if (_stopwatch.ElapsedMilliseconds > 0 || !string.Equals(status, "start", StringComparison.Ordinal))
        {
            text += $" elapsedMs={_stopwatch.ElapsedMilliseconds}";
        }

        if (!string.IsNullOrWhiteSpace(context))
        {
            text += " " + context;
        }

        if (exception is not null)
        {
            text += " error=" + exception.Message;
        }

        _appendUiLog(text);
    }

    private static IReadOnlyList<(string Key, object? Value)> CombineTags(
        IReadOnlyList<(string Key, object? Value)> tags,
        params (string Key, object? Value)[] extra)
    {
        if (extra.Length == 0)
        {
            return tags;
        }

        var combined = new List<(string Key, object? Value)>(tags.Count + extra.Length);
        combined.AddRange(tags);
        combined.AddRange(extra);
        return combined;
    }

    private static string FormatTags(IReadOnlyList<(string Key, object? Value)> tags)
    {
        if (tags.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(tags.Count);
        foreach (var (key, value) in tags)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null)
            {
                continue;
            }

            var text = value switch
            {
                bool boolValue => boolValue ? "true" : "false",
                _ => value.ToString()
            };

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            parts.Add($"{key.Trim()}={text.Trim()}");
        }

        return string.Join(" ", parts);
    }

    public void Dispose()
    {
    }
}
