using System.Text;

namespace SimsModDesktop.Application.Results;

internal static class CsvHelpers
{
    public static IReadOnlyList<Dictionary<string, string>> ReadRows(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2)
        {
            return Array.Empty<Dictionary<string, string>>();
        }

        var headers = ParseCsvLine(lines[0]);
        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var values = ParseCsvLine(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Count; c++)
            {
                var value = c < values.Count ? values[c] : string.Empty;
                row[headers[c]] = value;
            }

            rows.Add(row);
        }

        return rows;
    }

    public static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            else
            {
                if (ch == ',')
                {
                    values.Add(sb.ToString());
                    sb.Clear();
                }
                else if (ch == '"')
                {
                    inQuotes = true;
                }
                else
                {
                    sb.Append(ch);
                }
            }
        }

        values.Add(sb.ToString());
        return values;
    }
}
