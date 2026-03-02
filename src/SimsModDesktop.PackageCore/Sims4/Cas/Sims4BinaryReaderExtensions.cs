using System.Text;

namespace SimsModDesktop.PackageCore;

public static class Sims4BinaryReaderExtensions
{
    public static Sims4Tgi ReadTgi(this BinaryReader reader, Sims4TgiSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return sequence switch
        {
            Sims4TgiSequence.Tgi => new Sims4Tgi(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt64()),
            Sims4TgiSequence.Igt => ReadIgt(reader),
            Sims4TgiSequence.Itg => ReadItg(reader),
            _ => throw new ArgumentOutOfRangeException(nameof(sequence))
        };
    }

    public static ushort ReadUInt16BigEndian(this BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var bytes = reader.ReadBytes(sizeof(ushort));
        if (bytes.Length != sizeof(ushort))
        {
            throw new EndOfStreamException("Unexpected end of stream.");
        }

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToUInt16(bytes, 0);
    }

    public static string ReadLengthPrefixedStringUtf16BigEndian(this BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        using var stringReader = new BinaryReader(reader.BaseStream, Encoding.BigEndianUnicode, leaveOpen: true);
        return stringReader.ReadString();
    }

    public static string ReadSizedStringUtf16BigEndian(this BinaryReader reader, int byteLength)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (byteLength <= 0)
        {
            return string.Empty;
        }

        var bytes = reader.ReadBytes(byteLength);
        if (bytes.Length != byteLength)
        {
            throw new EndOfStreamException("Unexpected end of stream.");
        }

        return Encoding.BigEndianUnicode.GetString(bytes).TrimEnd('\0');
    }

    public static bool TryReadAtOffset<T>(
        this BinaryReader reader,
        long offset,
        Func<BinaryReader, T> read,
        out T? value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(read);

        var originalPosition = reader.BaseStream.Position;
        try
        {
            if (offset < 0 || offset > reader.BaseStream.Length)
            {
                value = default;
                return false;
            }

            reader.BaseStream.Position = offset;
            value = read(reader);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
        finally
        {
            reader.BaseStream.Position = originalPosition;
        }
    }

    private static Sims4Tgi ReadIgt(BinaryReader reader)
    {
        var instance = reader.ReadUInt64();
        var group = reader.ReadUInt32();
        var type = reader.ReadUInt32();
        return new Sims4Tgi(type, group, instance);
    }

    private static Sims4Tgi ReadItg(BinaryReader reader)
    {
        var instance = reader.ReadUInt64();
        var type = reader.ReadUInt32();
        var group = reader.ReadUInt32();
        return new Sims4Tgi(type, group, instance);
    }
}
