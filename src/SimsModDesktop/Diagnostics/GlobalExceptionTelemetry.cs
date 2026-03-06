using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Logging;

namespace SimsModDesktop.Diagnostics;

internal static class GlobalExceptionTelemetry
{
    private const string RuntimeUnhandledExceptionEvent = "runtime.unhandled_exception";
    private const string RuntimeUiDispatcherUnhandledExceptionEvent = "runtime.ui_dispatcher_unhandled_exception";
    private const string RuntimeUnobservedTaskExceptionEvent = "runtime.unobserved_task_exception";

    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static ILogger? _logger;
    private static bool _isInstalled;
    private static bool _isDispatcherHooked;

    public static void Install()
    {
        lock (Sync)
        {
            if (_isInstalled)
            {
                return;
            }

            _isInstalled = true;
        }

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static void AttachAvaloniaDispatcherHook()
    {
        lock (Sync)
        {
            if (_isDispatcherHooked)
            {
                return;
            }

            _isDispatcherHooked = true;
        }

        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
    }

    public static void BindLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        lock (Sync)
        {
            _logger = logger;
        }
    }

    private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        var exception = args.ExceptionObject as Exception
            ?? new InvalidOperationException(
                $"Unhandled exception object type: {args.ExceptionObject?.GetType().FullName ?? "null"}");
        var fatality = EvaluateAppDomainFatality(args, exception);
        var dump = fatality.IsFatal
            ? TryCaptureCrashDump("appdomain_unhandled", exception)
            : CrashDumpCaptureResult.Skipped("skipped_non_fatal");

        WriteCritical(
            exception,
            RuntimeUnhandledExceptionEvent,
            ("source", "AppDomain.CurrentDomain.UnhandledException"),
            ("isTerminating", args.IsTerminating),
            ("isFatal", fatality.IsFatal),
            ("fatalReason", fatality.Reason),
            ("dumpStatus", dump.Status),
            ("dumpPath", dump.Path),
            ("dumpError", dump.Error));
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        var fatality = EvaluateTaskSchedulerFatality(args.Exception, args.Observed);
        var dump = fatality.IsFatal
            ? TryCaptureCrashDump("task_unobserved_exception", args.Exception)
            : CrashDumpCaptureResult.Skipped("skipped_non_fatal");

        WriteCritical(
            args.Exception,
            RuntimeUnobservedTaskExceptionEvent,
            ("source", "TaskScheduler.UnobservedTaskException"),
            ("isObserved", args.Observed),
            ("isFatal", fatality.IsFatal),
            ("fatalReason", fatality.Reason),
            ("dumpStatus", dump.Status),
            ("dumpPath", dump.Path),
            ("dumpError", dump.Error));
    }

    private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        var fatality = EvaluateUiDispatcherFatality(args.Exception, args.Handled);
        var dump = fatality.IsFatal
            ? TryCaptureCrashDump("ui_dispatcher_unhandled", args.Exception)
            : CrashDumpCaptureResult.Skipped("skipped_non_fatal");

        WriteCritical(
            args.Exception,
            RuntimeUiDispatcherUnhandledExceptionEvent,
            ("source", "Dispatcher.UIThread.UnhandledException"),
            ("isHandled", args.Handled),
            ("isFatal", fatality.IsFatal),
            ("fatalReason", fatality.Reason),
            ("dumpStatus", dump.Status),
            ("dumpPath", dump.Path),
            ("dumpError", dump.Error));
    }

    private static void WriteCritical(Exception exception, string eventName, params (string Key, object? Value)[] tags)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var debuggerAttached = Debugger.IsAttached;
        var details = BuildDetails(tags);

        try
        {
            var logger = GetLogger();
            if (logger is not null)
            {
                logger.LogCritical(
                    exception,
                    "{Event} debuggerAttached={DebuggerAttached} details={Details}",
                    eventName,
                    debuggerAttached,
                    details);
                return;
            }

            WriteFallbackJson(eventName, exception, debuggerAttached, details);
        }
        catch (Exception loggingFailure)
        {
            var extendedDetails = string.IsNullOrWhiteSpace(details)
                ? $"loggerWriteFailure={loggingFailure.GetType().Name}"
                : $"{details} loggerWriteFailure={loggingFailure.GetType().Name}";
            WriteFallbackJson(eventName, exception, debuggerAttached, extendedDetails);
        }
    }

    private static ILogger? GetLogger()
    {
        lock (Sync)
        {
            return _logger;
        }
    }

    private static CrashDumpCaptureResult TryCaptureCrashDump(string reason, Exception exception)
    {
        if (!OperatingSystem.IsWindows())
        {
            return CrashDumpCaptureResult.Skipped("skipped_non_windows");
        }

        if (CrashDumpWriter.TryWrite(reason, exception, out var dumpPath, out var dumpError))
        {
            return new CrashDumpCaptureResult("created", dumpPath, null);
        }

        return new CrashDumpCaptureResult("failed", null, dumpError ?? "unknown");
    }

    private static FatalityVerdict EvaluateAppDomainFatality(UnhandledExceptionEventArgs args, Exception exception)
    {
        if (args.IsTerminating)
        {
            return new FatalityVerdict(true, "appdomain_is_terminating");
        }

        var typeVerdict = EvaluateFatalType(exception);
        if (typeVerdict.IsFatal)
        {
            return typeVerdict;
        }

        return new FatalityVerdict(false, "appdomain_non_terminating_exception");
    }

    private static FatalityVerdict EvaluateUiDispatcherFatality(Exception exception, bool isHandled)
    {
        if (!isHandled)
        {
            return new FatalityVerdict(true, "ui_exception_unhandled");
        }

        var typeVerdict = EvaluateFatalType(exception);
        if (typeVerdict.IsFatal)
        {
            return new FatalityVerdict(true, $"ui_handled_but_{typeVerdict.Reason}");
        }

        return new FatalityVerdict(false, "ui_exception_handled");
    }

    private static FatalityVerdict EvaluateTaskSchedulerFatality(Exception exception, bool isObserved)
    {
        var typeVerdict = EvaluateFatalType(exception);
        if (typeVerdict.IsFatal)
        {
            return new FatalityVerdict(true, $"task_exception_{typeVerdict.Reason}");
        }

        if (isObserved)
        {
            return new FatalityVerdict(false, "task_exception_observed");
        }

        return new FatalityVerdict(false, "task_exception_unobserved_non_fatal");
    }

    private static FatalityVerdict EvaluateFatalType(Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            foreach (var inner in aggregateException.Flatten().InnerExceptions)
            {
                var innerVerdict = EvaluateFatalType(inner);
                if (innerVerdict.IsFatal)
                {
                    return new FatalityVerdict(true, $"aggregate_contains_{innerVerdict.Reason}");
                }
            }

            return new FatalityVerdict(false, "aggregate_non_fatal");
        }

        if (exception is AccessViolationException)
        {
            return new FatalityVerdict(true, "access_violation");
        }

        if (exception is SEHException)
        {
            return new FatalityVerdict(true, "seh_exception");
        }

        if (exception is OutOfMemoryException)
        {
            return new FatalityVerdict(true, "out_of_memory");
        }

        return new FatalityVerdict(false, "non_fatal_exception_type");
    }

    private static string BuildDetails((string Key, object? Value)[] tags)
    {
        if (tags.Length == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(tags.Length);
        foreach (var (key, value) in tags)
        {
            var normalizedKey = string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim();
            parts.Add($"{normalizedKey}={value?.ToString() ?? "null"}");
        }

        return string.Join(' ', parts);
    }

    private static void WriteFallbackJson(string eventName, Exception exception, bool debuggerAttached, string details)
    {
        try
        {
            var path = new StructuredFileLoggerOptions().FilePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["level"] = LogLevel.Critical.ToString(),
                ["category"] = typeof(GlobalExceptionTelemetry).FullName,
                ["eventId"] = 0,
                ["eventName"] = eventName,
                ["message"] = $"{eventName} debuggerAttached={debuggerAttached} details={details}",
                ["exception"] = exception.ToString()
            };

            var line = JsonSerializer.Serialize(entry, JsonOptions);
            lock (Sync)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception fallbackException)
        {
            Trace.WriteLine($"[{eventName}] crash log fallback failed: {fallbackException}");
            Trace.WriteLine(exception.ToString());
        }
    }

    private readonly record struct FatalityVerdict(bool IsFatal, string Reason);

    private readonly record struct CrashDumpCaptureResult(string Status, string? Path, string? Error)
    {
        public static CrashDumpCaptureResult Skipped(string status) => new(status, null, null);
    }
}
