using System.Text;

namespace SimsModDesktop.PackageCore;

public static class Sims4StblParser
{
    private static ReadOnlySpan<byte> Magic => "STBL"u8;

    public static bool TryParse(
        ReadOnlySpan<byte> bytes,
        out Sims4StblTable table,
        out string? error)
    {
        table = null!;
        error = null;

        if (bytes.Length < 4)
        {
            error = "STBL payload is too small.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            Dictionary<uint, string> entries;
            if (bytes.StartsWith(Magic))
            {
                _ = reader.ReadBytes(4);
                _ = reader.ReadUInt32(); // version/reserved
                entries = ReadEntries(reader);
            }
            else
            {
                entries = ReadEntries(reader);
            }

            table = new Sims4StblTable
            {
                Entries = entries
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse STBL: {ex.Message}";
            return false;
        }
    }

    private static Dictionary<uint, string> ReadEntries(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > 100_000)
        {
            throw new InvalidDataException("STBL entry count is invalid.");
        }

        var entries = new Dictionary<uint, string>(count);
        for (var index = 0; index < count; index++)
        {
            var key = reader.ReadUInt32();
            var byteCount = reader.ReadInt32();
            if (byteCount < 0 || byteCount > 4_194_304)
            {
                throw new InvalidDataException("STBL string length is invalid.");
            }

            var textBytes = reader.ReadBytes(byteCount);
            if (textBytes.Length != byteCount)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }

            entries[key] = Encoding.UTF8.GetString(textBytes);
        }

        return entries;
    }
}
