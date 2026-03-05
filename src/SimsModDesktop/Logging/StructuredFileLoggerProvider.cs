using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SimsModDesktop.Logging;

internal sealed class StructuredFileLoggerProvider : ILoggerProvider
{
    private readonly StructuredFileLoggerOptions _options;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private StreamWriter? _writer;

    public StructuredFileLoggerProvider(StructuredFileLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new StructuredFileLogger(this, string.IsNullOrWhiteSpace(categoryName) ? "Unknown" : categoryName.Trim());
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(
        string category,
        LogLevel logLevel,
        EventId eventId,
        string message,
        Exception? exception)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_sync)
        {
            _writer ??= CreateWriter(_options.FilePath);
            var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["level"] = logLevel.ToString(),
                ["category"] = category,
                ["eventId"] = eventId.Id,
                ["eventName"] = eventId.Name ?? string.Empty,
                ["message"] = message
            };

            if (exception is not null)
            {
                entry["exception"] = exception.ToString();
            }

            var line = JsonSerializer.Serialize(entry, _jsonOptions);
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    private static StreamWriter CreateWriter(string filePath)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache",
                "Logs",
                "application.log.jsonl")
            : filePath.Trim();

        var directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(normalizedPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        return new StreamWriter(stream);
    }

    private sealed class StructuredFileLogger : ILogger
    {
        private readonly StructuredFileLoggerProvider _provider;
        private readonly string _category;

        public StructuredFileLogger(StructuredFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            _provider.Write(_category, logLevel, eventId, message, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
