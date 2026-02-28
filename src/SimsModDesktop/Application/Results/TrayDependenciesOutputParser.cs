using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Results;

public sealed class TrayDependenciesOutputParser : IExecutionOutputParser
{
    public bool CanParse(SimsAction action) => action == SimsAction.TrayDependencies;

    public bool TryParse(
        ExecutionOutputParseContext context,
        out ActionResultEnvelope envelope,
        out string error)
    {
        envelope = null!;
        error = string.Empty;

        var csvPath = TryExtractCsvPath(context.LogText, "CSV:");
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            error = "Tray dependencies output CSV not found in logs.";
            return false;
        }

        var csvRows = CsvHelpers.ReadRows(csvPath);
        var rows = csvRows.Select(row =>
        {
            row.TryGetValue("PackagePath", out var packagePath);
            row.TryGetValue("Confidence", out var confidence);
            row.TryGetValue("MatchInstanceCount", out var matchInstanceCount);
            row.TryGetValue("MatchRatePct", out var matchRatePct);
            row.TryGetValue("PackageSizeBytes", out var packageSizeBytes);

            long? sizeBytes = null;
            if (long.TryParse(packageSizeBytes, out var parsedSize))
            {
                sizeBytes = parsedSize;
            }

            return new ActionResultRow
            {
                Name = Path.GetFileName(packagePath ?? string.Empty),
                Status = ParseStatusFromConfidence(confidence),
                SizeBytes = sizeBytes,
                PrimaryPath = packagePath ?? string.Empty,
                Confidence = confidence ?? string.Empty,
                Category = "TrayDependency",
                DependencyInfo = $"MatchInstanceCount={matchInstanceCount}",
                RawSummary = $"MatchRatePct={matchRatePct}"
            };
        }).ToArray();

        envelope = new ActionResultEnvelope
        {
            Action = SimsAction.TrayDependencies,
            Source = csvPath,
            Rows = rows
        };
        return true;
    }

    private static string ParseStatusFromConfidence(string? confidence)
    {
        if (string.IsNullOrWhiteSpace(confidence))
        {
            return "Unknown";
        }

        return confidence.Trim().ToLowerInvariant() switch
        {
            "high" => "Conflict",
            "medium" => "Review",
            "low" => "Safe",
            _ => "Unknown"
        };
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
