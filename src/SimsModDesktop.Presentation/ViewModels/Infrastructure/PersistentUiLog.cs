using System.Text;

namespace SimsModDesktop.Presentation.ViewModels.Infrastructure;

internal static class PersistentUiLog
{
    private static readonly object Sync = new();
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SimsModDesktop",
        "Cache",
        "Logs",
        "ui.log");

    public static string PathOnDisk => LogFilePath;

    public static void ResetIfExists()
    {
        try
        {
            lock (Sync)
            {
                if (!File.Exists(LogFilePath))
                {
                    return;
                }

                var info = new FileInfo(LogFilePath);
                if (info.Length == 0)
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

    public static void Append(string source, string message)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            lock (Sync)
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
        catch
        {
            // Ignore logging failures so UI actions are never blocked by disk IO issues.
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
}
