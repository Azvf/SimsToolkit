using System.Security.Cryptography;
using System.Text;

namespace SimsModDesktop.Models;

internal static class TrayContentFingerprint
{
    public static string Compute(IEnumerable<string> sourceFiles)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);

        var builder = new StringBuilder();

        foreach (var sourceFile in sourceFiles
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var file = new FileInfo(sourceFile);
            builder
                .Append(file.FullName)
                .Append('|')
                .Append(file.Exists ? file.Length : 0)
                .Append('|')
                .Append(file.Exists ? file.LastWriteTimeUtc.Ticks : 0)
                .Append('\n');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
    }
}
