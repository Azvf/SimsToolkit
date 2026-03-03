using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Results;

public sealed class FindDupOutputParser : IExecutionOutputParser
{
    public bool CanParse(SimsAction action) => action == SimsAction.FindDuplicates;

    public bool TryParse(
        ExecutionOutputParseContext context,
        out ActionResultEnvelope envelope,
        out string error)
    {
        envelope = null!;
        error = string.Empty;

        var csvPath = TryExtractCsvPath(context.LogText, "Exported to:");
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            error = "Find duplicates CSV not found. Configure OutputCsv for structured results.";
            return false;
        }

        var csvRows = CsvHelpers.ReadRows(csvPath);
        var rows = csvRows.Select(row =>
        {
            row.TryGetValue("FilePath", out var filePath);
            row.TryGetValue("Md5Hash", out var md5Hash);
            row.TryGetValue("FileSize", out var fileSizeRaw);
            row.TryGetValue("GroupId", out var groupIdRaw);
            row.TryGetValue("FileCount", out var fileCountRaw);

            long? fileSize = null;
            if (long.TryParse(fileSizeRaw, out var sizeParsed))
            {
                fileSize = sizeParsed;
            }

            return new ActionResultRow
            {
                Name = Path.GetFileName(filePath ?? string.Empty),
                Status = "Duplicate",
                SizeBytes = fileSize,
                PrimaryPath = filePath ?? string.Empty,
                Category = "FindDuplicates",
                Hash = md5Hash ?? string.Empty,
                RawSummary = $"Group={groupIdRaw}, Count={fileCountRaw}"
            };
        }).ToArray();

        envelope = new ActionResultEnvelope
        {
            Action = SimsAction.FindDuplicates,
            Source = csvPath,
            Rows = rows
        };
        return true;
    }

    private static string? TryExtractCsvPath(string logText, string marker)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return null;
        }

        var lines = logText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var path = line[(index + marker.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return null;
    }
}
