using System.Text;

namespace SimsModDesktop.Presentation.ViewModels.Infrastructure;

public sealed class UiLogSink : IUiLogSink
{
    private readonly object _sync = new();
    private readonly Dictionary<string, StringBuilder> _buffers = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SimsModDesktop",
        "Cache",
        "Logs",
        "ui.log");

    public void ResetAll()
    {
        try
        {
            lock (_sync)
            {
                _buffers.Clear();
                if (!File.Exists(LogFilePath))
                {
                    return;
                }

                using var _ = new FileStream(
                    LogFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite);
            }
        }
        catch
        {
            // Ignore logging failures so UI actions are never blocked by disk IO issues.
        }
    }

    public void Append(string source, string message)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                var normalizedSource = source.Trim();
                var buffer = GetOrCreateBuffer(normalizedSource);
                foreach (var line in SplitLines(message))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    buffer.AppendLine(line);
                }

                WriteToDisk(normalizedSource, message);
            }
        }
        catch
        {
            // Ignore logging failures so UI actions are never blocked by disk IO issues.
        }
    }

    public void ClearSource(string source, bool appendClearedMarker)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                _buffers[source.Trim()] = new StringBuilder();
            }

            if (appendClearedMarker)
            {
                Append(source, "[log-cleared]");
            }
        }
        catch
        {
            // Ignore logging failures so UI actions are never blocked by disk IO issues.
        }
    }

    public string GetSourceText(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        lock (_sync)
        {
            return _buffers.TryGetValue(source.Trim(), out var buffer)
                ? buffer.ToString()
                : string.Empty;
        }
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line.TrimEnd();
        }
    }

    private StringBuilder GetOrCreateBuffer(string source)
    {
        if (_buffers.TryGetValue(source, out var buffer))
        {
            return buffer;
        }

        buffer = new StringBuilder();
        _buffers[source] = buffer;
        return buffer;
    }

    private static void WriteToDisk(string source, string message)
    {
        var directory = Path.GetDirectoryName(LogFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(
            LogFilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        foreach (var line in SplitLines(message))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            writer.Write('[');
            writer.Write(timestamp);
            writer.Write("] [");
            writer.Write(source);
            writer.Write("] ");
            writer.WriteLine(line);
        }
    }
}
