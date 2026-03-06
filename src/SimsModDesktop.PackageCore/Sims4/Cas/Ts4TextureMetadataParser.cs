using System.Buffers.Binary;

namespace SimsModDesktop.PackageCore;

public enum Ts4TextureContainerKind
{
    Unknown = 0,
    Dds = 1,
    Dst = 2,
    Rle2 = 3,
    Rles = 4,
    Lrle = 5
}

public sealed class Ts4TextureReadMetadata
{
    public required Ts4TextureContainerKind ContainerKind { get; init; }
    public required uint ContainerVersion { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int MipCount { get; init; }
    public required bool Mip0Decoded { get; init; }
    public required int Mip0PixelCount { get; init; }
    public required byte AlphaMin { get; init; }
    public required byte AlphaMax { get; init; }
    public required int AlphaNonZeroPixelCount { get; init; }
    public string DecodeError { get; init; } = string.Empty;
}

public sealed class Ts4TextureMetadataParser : ITS4ResourceParser<Ts4TextureReadMetadata>
{
    private const uint DdsMagic = 0x20534444;
    private const uint LrleMagic = 0x454C524C;
    private const uint RleDxt5 = 0x35545844;
    private const uint RleL8 = 0x2020384C;
    private const uint Rle2Version = 0x32454C52;
    private const uint RlesVersion = 0x53454C52;
    private const uint LrleV2 = 0x32303056;

    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4TextureReadMetadata result, out string? error)
    {
        result = null!;
        error = null;

        try
        {
            if (key.Type == Sims4ResourceTypeRegistry.Lrle)
            {
                result = ParseLrle(bytes);
                return true;
            }

            if (key.Type == Sims4ResourceTypeRegistry.Rle2 || key.Type == Sims4ResourceTypeRegistry.Rles)
            {
                result = ParseRle(bytes);
                return true;
            }

            if (key.Type == Sims4ResourceTypeRegistry.Dds || key.Type == Sims4ResourceTypeRegistry.DdsUncompressed)
            {
                result = ParseDds(bytes);
                return true;
            }

            if (key.Type == Sims4ResourceTypeRegistry.Dst)
            {
                result = new Ts4TextureReadMetadata
                {
                    ContainerKind = Ts4TextureContainerKind.Dst,
                    ContainerVersion = 0,
                    Width = 0,
                    Height = 0,
                    MipCount = 0,
                    Mip0Decoded = false,
                    Mip0PixelCount = 0,
                    AlphaMin = 0,
                    AlphaMax = 0,
                    AlphaNonZeroPixelCount = 0,
                    DecodeError = "DST decode is not implemented."
                };
                return true;
            }

            error = "Resource is not a supported texture type.";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse texture metadata: {ex.Message}";
            return false;
        }
    }

    private static Ts4TextureReadMetadata ParseDds(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 32 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[..4]) != DdsMagic)
        {
            throw new InvalidDataException("Invalid DDS payload.");
        }

        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12, 4));
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(16, 4));
        var mipCount = Math.Max(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(28, 4)));
        return new Ts4TextureReadMetadata
        {
            ContainerKind = Ts4TextureContainerKind.Dds,
            ContainerVersion = 0,
            Width = Math.Max(0, width),
            Height = Math.Max(0, height),
            MipCount = mipCount,
            Mip0Decoded = false,
            Mip0PixelCount = Math.Max(0, width) * Math.Max(0, height),
            AlphaMin = 0,
            AlphaMax = 0,
            AlphaNonZeroPixelCount = 0
        };
    }

    private static Ts4TextureReadMetadata ParseLrle(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadUInt32();
        if (magic != LrleMagic)
        {
            throw new InvalidDataException("Invalid LRLE magic.");
        }

        var version = reader.ReadUInt32();
        if (version != 0 && version != LrleV2)
        {
            throw new InvalidDataException($"Unsupported LRLE version 0x{version:X8}.");
        }

        var width = reader.ReadUInt16();
        var height = reader.ReadUInt16();
        var mipCount = checked((int)reader.ReadUInt32());
        if (mipCount <= 0 || mipCount > 32)
        {
            throw new InvalidDataException("Invalid LRLE mip count.");
        }

        var mipOffsets = new int[mipCount];
        for (var i = 0; i < mipCount; i++)
        {
            mipOffsets[i] = reader.ReadInt32();
            if (mipOffsets[i] < 0)
            {
                throw new InvalidDataException("Invalid LRLE mip offset.");
            }
        }

        byte[][]? palette = null;
        if (version == LrleV2)
        {
            var pixelCount = checked((int)reader.ReadUInt32());
            if (pixelCount < 0 || pixelCount > ushort.MaxValue)
            {
                throw new InvalidDataException("Invalid LRLE palette size.");
            }

            palette = new byte[pixelCount][];
            for (var i = 0; i < pixelCount; i++)
            {
                palette[i] = reader.ReadBytes(4);
                if (palette[i].Length != 4)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading LRLE palette.");
                }
            }
        }

        var mipData = new byte[mipCount][];
        for (var i = 0; i < mipCount; i++)
        {
            var start = mipOffsets[i];
            var end = i < mipCount - 1 ? mipOffsets[i + 1] : checked((int)(stream.Length - stream.Position + start));
            var length = end - start;
            if (length < 0)
            {
                throw new InvalidDataException("Invalid LRLE mip payload length.");
            }

            mipData[i] = reader.ReadBytes(length);
            if (mipData[i].Length != length)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading LRLE mip payload.");
            }
        }

        var rgba = Array.Empty<byte>();
        var decoded = false;
        var decodeError = string.Empty;
        if (mipData.Length > 0)
        {
            decoded = version == 0
                ? TryDecodeLrleMipV1(mipData[0], width, height, out rgba, out decodeError)
                : TryDecodeLrleMipV2(mipData[0], width, height, palette ?? Array.Empty<byte[]>(), out rgba, out decodeError);
        }

        CalculateAlphaStats(rgba, out var alphaMin, out var alphaMax, out var alphaNonZero);
        return new Ts4TextureReadMetadata
        {
            ContainerKind = Ts4TextureContainerKind.Lrle,
            ContainerVersion = version,
            Width = width,
            Height = height,
            MipCount = mipCount,
            Mip0Decoded = decoded,
            Mip0PixelCount = width * height,
            AlphaMin = alphaMin,
            AlphaMax = alphaMax,
            AlphaNonZeroPixelCount = alphaNonZero,
            DecodeError = decoded ? string.Empty : decodeError
        };
    }

    private static Ts4TextureReadMetadata ParseRle(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream);

        var format = reader.ReadUInt32();
        if (format != RleDxt5 && format != RleL8)
        {
            throw new InvalidDataException($"Unsupported RLE texture format 0x{format:X8}.");
        }

        var version = reader.ReadUInt32();
        if (version != Rle2Version && version != RlesVersion)
        {
            throw new InvalidDataException($"Unsupported RLE version 0x{version:X8}.");
        }

        var width = reader.ReadUInt16();
        var height = reader.ReadUInt16();
        var mipCount = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        if (mipCount == 0 || mipCount > 32)
        {
            throw new InvalidDataException("Invalid RLE mip count.");
        }

        var headers = new MipHeader[mipCount + 1];
        for (var i = 0; i < mipCount; i++)
        {
            if (format == RleL8)
            {
                headers[i] = new MipHeader
                {
                    CommandOffset = reader.ReadInt32(),
                    Offset0 = reader.ReadInt32()
                };
            }
            else
            {
                headers[i] = new MipHeader
                {
                    CommandOffset = reader.ReadInt32(),
                    Offset2 = reader.ReadInt32(),
                    Offset3 = reader.ReadInt32(),
                    Offset0 = reader.ReadInt32(),
                    Offset1 = reader.ReadInt32(),
                    Offset4 = version == RlesVersion ? reader.ReadInt32() : 0
                };
            }
        }

        if (format == RleL8)
        {
            headers[mipCount] = new MipHeader
            {
                CommandOffset = headers[0].Offset0,
                Offset0 = checked((int)stream.Length)
            };
        }
        else
        {
            headers[mipCount] = new MipHeader
            {
                CommandOffset = headers[0].Offset2,
                Offset2 = headers[0].Offset3,
                Offset3 = headers[0].Offset0,
                Offset0 = headers[0].Offset1,
                Offset1 = version == RlesVersion ? headers[0].Offset4 : checked((int)stream.Length),
                Offset4 = version == RlesVersion ? checked((int)stream.Length) : 0
            };
        }

        var raw = bytes.ToArray();
        var rgba = Array.Empty<byte>();
        var decoded = false;
        var decodeError = string.Empty;
        if (mipCount > 0)
        {
            if (format == RleDxt5)
            {
                decoded = version == RlesVersion
                    ? TryDecodeRlesMip0(raw, width, height, headers[0], headers[1], out rgba, out decodeError)
                    : TryDecodeRle2Mip0(raw, width, height, headers[0], headers[1], out rgba, out decodeError);
            }
            else
            {
                decodeError = "L8 RLE variant is not decoded in this phase.";
            }
        }

        CalculateAlphaStats(rgba, out var alphaMin, out var alphaMax, out var alphaNonZero);
        return new Ts4TextureReadMetadata
        {
            ContainerKind = version == RlesVersion ? Ts4TextureContainerKind.Rles : Ts4TextureContainerKind.Rle2,
            ContainerVersion = version,
            Width = width,
            Height = height,
            MipCount = mipCount,
            Mip0Decoded = decoded,
            Mip0PixelCount = width * height,
            AlphaMin = alphaMin,
            AlphaMax = alphaMax,
            AlphaNonZeroPixelCount = alphaNonZero,
            DecodeError = decoded ? string.Empty : decodeError
        };
    }

    private static bool TryDecodeLrleMipV1(byte[] mip, int width, int height, out byte[] rgba, out string error)
    {
        rgba = Array.Empty<byte>();
        error = string.Empty;
        try
        {
            var pixels = new byte[checked(width * height * 4)];
            var pointer = 0;
            var pixelPointer = 0;
            while (pointer < mip.Length)
            {
                var instruction = mip[pointer] & 3;
                switch (instruction)
                {
                    case 0:
                    {
                        var count = GetLrlePixelRunLength(mip, ref pointer);
                        pointer++;
                        FillZeros(pixels, ref pixelPointer, checked(count * 4));
                        break;
                    }
                    case 1:
                    {
                        var count = mip[pointer] >> 2;
                        pointer++;
                        CopyBytes(mip, ref pointer, pixels, ref pixelPointer, checked(count * 4));
                        break;
                    }
                    case 2:
                    {
                        var count = GetLrlePixelRunLength(mip, ref pointer);
                        pointer++;
                        for (var i = 0; i < count; i++)
                        {
                            CopyBytes(mip, pointer, pixels, ref pixelPointer, 4);
                        }

                        pointer += 4;
                        break;
                    }
                    case 3:
                    {
                        var count = mip[pointer] >> 2;
                        pointer++;
                        var embedded = ReadEmbeddedLrleRun(mip, ref count, ref pointer);
                        for (var i = 0; i < count; i++)
                        {
                            EnsureWriteCapacity(pixels, pixelPointer, 4);
                            pixels[pixelPointer] = embedded[i];
                            pixels[pixelPointer + 1] = embedded[i + count];
                            pixels[pixelPointer + 2] = embedded[i + (2 * count)];
                            pixels[pixelPointer + 3] = embedded[i + (3 * count)];
                            pixelPointer += 4;
                        }

                        break;
                    }
                    default:
                        throw new InvalidDataException($"Unknown LRLE v1 instruction {instruction}.");
                }
            }

            rgba = RearrangeLrleBlocks(pixels, width, height);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryDecodeLrleMipV2(byte[] mip, int width, int height, IReadOnlyList<byte[]> palette, out byte[] rgba, out string error)
    {
        rgba = Array.Empty<byte>();
        error = string.Empty;
        try
        {
            var pixels = new byte[checked(width * height * 4)];
            var pointer = 0;
            var pixelPointer = 0;
            while (pointer < mip.Length)
            {
                if ((mip[pointer] & 0x01) > 0 && (mip[pointer] & 0x02) > 0)
                {
                    var count = GetLrlePixelRunLength(mip, ref pointer);
                    pointer++;
                    for (var i = 0; i < count; i++)
                    {
                        CopyBytes(mip, ref pointer, pixels, ref pixelPointer, 4);
                    }

                    continue;
                }

                if ((mip[pointer] & 0x01) == 0 && (mip[pointer] & 0x02) > 0 && (mip[pointer] & 0x04) > 0)
                {
                    var count = GetLrleRepeatRunLength(mip, ref pointer);
                    pointer++;
                    for (var i = 0; i < count; i++)
                    {
                        CopyBytes(mip, pointer, pixels, ref pixelPointer, 4);
                    }

                    pointer += 4;
                    continue;
                }

                if ((mip[pointer] & 0x01) > 0 && (mip[pointer] & 0x02) == 0)
                {
                    var count = GetLrlePixelRunLength(mip, ref pointer);
                    pointer++;
                    for (var i = 0; i < count; i++)
                    {
                        var index = GetLrleColorIndex(mip, ref pointer);
                        EnsurePaletteIndex(palette, index);
                        CopyBytes(palette[index], 0, pixels, ref pixelPointer, 4);
                        pointer++;
                    }

                    continue;
                }

                if ((mip[pointer] & 0x02) > 0 && (mip[pointer] & 0x01) == 0 && (mip[pointer] & 0x04) == 0)
                {
                    var count = GetLrleRepeatRunLength(mip, ref pointer);
                    pointer++;
                    var index = mip[pointer];
                    EnsurePaletteIndex(palette, index);
                    for (var i = 0; i < count; i++)
                    {
                        CopyBytes(palette[index], 0, pixels, ref pixelPointer, 4);
                    }

                    pointer++;
                    continue;
                }

                if ((mip[pointer] & 0x04) > 0 && (mip[pointer] & 0x01) == 0 && (mip[pointer] & 0x02) == 0)
                {
                    var count = GetLrleRepeatRunLength(mip, ref pointer);
                    pointer++;
                    EnsureReadCapacity(mip, pointer, 2);
                    var index = BitConverter.ToUInt16(mip, pointer);
                    EnsurePaletteIndex(palette, index);
                    for (var i = 0; i < count; i++)
                    {
                        CopyBytes(palette[index], 0, pixels, ref pixelPointer, 4);
                    }

                    pointer += 2;
                    continue;
                }

                throw new InvalidDataException($"Unknown LRLE v2 instruction 0x{mip[pointer]:X2}.");
            }

            rgba = RearrangeLrleBlocks(pixels, width, height);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryDecodeRle2Mip0(byte[] source, int width, int height, MipHeader mipHeader, MipHeader nextMipHeader, out byte[] rgba, out string error)
    {
        rgba = Array.Empty<byte>();
        error = string.Empty;
        try
        {
            var blocks = DecodeRle2Blocks(source, mipHeader, nextMipHeader, width, height);
            rgba = DecodeBc3Rgba(width, height, blocks);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryDecodeRlesMip0(byte[] source, int width, int height, MipHeader mipHeader, MipHeader nextMipHeader, out byte[] rgba, out string error)
    {
        rgba = Array.Empty<byte>();
        error = string.Empty;
        try
        {
            var blocks = DecodeRlesBlocks(source, mipHeader, nextMipHeader, width, height);
            rgba = DecodeBc3Rgba(width, height, blocks);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static byte[] DecodeRle2Blocks(byte[] source, MipHeader mipHeader, MipHeader nextMipHeader, int width, int height)
    {
        var blockCount = Math.Max(1, ((width + 3) / 4) * ((height + 3) / 4));
        var blocks = new byte[checked(blockCount * 16)];
        var writeOffset = 0;

        var blockOffset2 = mipHeader.Offset2;
        var blockOffset3 = mipHeader.Offset3;
        var blockOffset0 = mipHeader.Offset0;
        var blockOffset1 = mipHeader.Offset1;
        for (var commandOffset = mipHeader.CommandOffset; commandOffset < nextMipHeader.CommandOffset; commandOffset += 2)
        {
            EnsureReadCapacity(source, commandOffset, 2);
            var command = BitConverter.ToUInt16(source, commandOffset);
            var op = command & 3;
            var count = command >> 2;
            switch (op)
            {
                case 0:
                    for (var i = 0; i < count; i++)
                    {
                        WriteBlock(blocks, ref writeOffset, FullTransparentAlpha);
                        WriteBlock(blocks, ref writeOffset, FullTransparentWhite);
                    }

                    break;
                case 1:
                    for (var i = 0; i < count; i++)
                    {
                        CopyBlock(source, ref blockOffset0, blocks, ref writeOffset, 2);
                        CopyBlock(source, ref blockOffset1, blocks, ref writeOffset, 6);
                        CopyBlock(source, ref blockOffset2, blocks, ref writeOffset, 4);
                        CopyBlock(source, ref blockOffset3, blocks, ref writeOffset, 4);
                    }

                    break;
                case 2:
                    for (var i = 0; i < count; i++)
                    {
                        WriteBlock(blocks, ref writeOffset, FullOpaqueAlpha);
                        CopyBlock(source, ref blockOffset2, blocks, ref writeOffset, 4);
                        CopyBlock(source, ref blockOffset3, blocks, ref writeOffset, 4);
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unsupported RLE2 opcode {op}.");
            }
        }

        if (blockOffset0 != nextMipHeader.Offset0 ||
            blockOffset1 != nextMipHeader.Offset1 ||
            blockOffset2 != nextMipHeader.Offset2 ||
            blockOffset3 != nextMipHeader.Offset3)
        {
            throw new InvalidDataException("RLE2 block offsets do not match mip sentinel.");
        }

        if (writeOffset != blocks.Length)
        {
            throw new InvalidDataException("RLE2 mip block count does not match expected mip dimensions.");
        }

        return blocks;
    }

    private static byte[] DecodeRlesBlocks(byte[] source, MipHeader mipHeader, MipHeader nextMipHeader, int width, int height)
    {
        var blockCount = Math.Max(1, ((width + 3) / 4) * ((height + 3) / 4));
        var blocks = new byte[checked(blockCount * 16)];
        var writeOffset = 0;

        var blockOffset2 = mipHeader.Offset2;
        var blockOffset3 = mipHeader.Offset3;
        var blockOffset0 = mipHeader.Offset0;
        var blockOffset1 = mipHeader.Offset1;
        var blockOffset4 = mipHeader.Offset4;
        for (var commandOffset = mipHeader.CommandOffset; commandOffset < nextMipHeader.CommandOffset; commandOffset += 2)
        {
            EnsureReadCapacity(source, commandOffset, 2);
            var command = BitConverter.ToUInt16(source, commandOffset);
            var op = command & 3;
            var count = command >> 2;
            switch (op)
            {
                case 0:
                    for (var i = 0; i < count; i++)
                    {
                        WriteBlock(blocks, ref writeOffset, FullTransparentAlpha);
                        WriteBlock(blocks, ref writeOffset, FullTransparentBlack);
                    }

                    break;
                case 1:
                    for (var i = 0; i < count; i++)
                    {
                        CopyBlock(source, ref blockOffset0, blocks, ref writeOffset, 2);
                        CopyBlock(source, ref blockOffset1, blocks, ref writeOffset, 6);
                        CopyBlock(source, ref blockOffset2, blocks, ref writeOffset, 4);
                        CopyBlock(source, ref blockOffset3, blocks, ref writeOffset, 4);
                        blockOffset4 += 16;
                    }

                    break;
                case 2:
                    for (var i = 0; i < count; i++)
                    {
                        CopyBlock(source, ref blockOffset0, blocks, ref writeOffset, 2);
                        CopyBlock(source, ref blockOffset1, blocks, ref writeOffset, 6);
                        CopyBlock(source, ref blockOffset2, blocks, ref writeOffset, 4);
                        CopyBlock(source, ref blockOffset3, blocks, ref writeOffset, 4);
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unsupported RLES opcode {op}.");
            }
        }

        if (blockOffset0 != nextMipHeader.Offset0 ||
            blockOffset1 != nextMipHeader.Offset1 ||
            blockOffset2 != nextMipHeader.Offset2 ||
            blockOffset3 != nextMipHeader.Offset3 ||
            blockOffset4 != nextMipHeader.Offset4)
        {
            throw new InvalidDataException("RLES block offsets do not match mip sentinel.");
        }

        if (writeOffset != blocks.Length)
        {
            throw new InvalidDataException("RLES mip block count does not match expected mip dimensions.");
        }

        return blocks;
    }

    private static byte[] DecodeBc3Rgba(int width, int height, byte[] blocks)
    {
        var blocksAcross = Math.Max(1, (width + 3) / 4);
        var blocksDown = Math.Max(1, (height + 3) / 4);
        var rgba = new byte[checked(width * height * 4)];
        for (var blockY = 0; blockY < blocksDown; blockY++)
        {
            for (var blockX = 0; blockX < blocksAcross; blockX++)
            {
                var blockIndex = ((blockY * blocksAcross) + blockX) * 16;
                DecodeBc3Block(blocks, blockIndex, rgba, width, height, blockX * 4, blockY * 4);
            }
        }

        return rgba;
    }

    private static void DecodeBc3Block(
        byte[] source,
        int blockOffset,
        byte[] targetRgba,
        int width,
        int height,
        int startX,
        int startY)
    {
        EnsureReadCapacity(source, blockOffset, 16);
        Span<byte> alphaPalette = stackalloc byte[8];
        alphaPalette[0] = source[blockOffset];
        alphaPalette[1] = source[blockOffset + 1];
        if (alphaPalette[0] > alphaPalette[1])
        {
            alphaPalette[2] = (byte)((6 * alphaPalette[0] + alphaPalette[1] + 3) / 7);
            alphaPalette[3] = (byte)((5 * alphaPalette[0] + 2 * alphaPalette[1] + 3) / 7);
            alphaPalette[4] = (byte)((4 * alphaPalette[0] + 3 * alphaPalette[1] + 3) / 7);
            alphaPalette[5] = (byte)((3 * alphaPalette[0] + 4 * alphaPalette[1] + 3) / 7);
            alphaPalette[6] = (byte)((2 * alphaPalette[0] + 5 * alphaPalette[1] + 3) / 7);
            alphaPalette[7] = (byte)((alphaPalette[0] + 6 * alphaPalette[1] + 3) / 7);
        }
        else
        {
            alphaPalette[2] = (byte)((4 * alphaPalette[0] + alphaPalette[1] + 2) / 5);
            alphaPalette[3] = (byte)((3 * alphaPalette[0] + 2 * alphaPalette[1] + 2) / 5);
            alphaPalette[4] = (byte)((2 * alphaPalette[0] + 3 * alphaPalette[1] + 2) / 5);
            alphaPalette[5] = (byte)((alphaPalette[0] + 4 * alphaPalette[1] + 2) / 5);
            alphaPalette[6] = 0;
            alphaPalette[7] = 255;
        }

        ulong alphaBits = 0;
        for (var i = 0; i < 6; i++)
        {
            alphaBits |= (ulong)source[blockOffset + 2 + i] << (8 * i);
        }

        var color0 = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(blockOffset + 8, 2));
        var color1 = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(blockOffset + 10, 2));
        var colorBits = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(blockOffset + 12, 4));

        Span<byte> colors = stackalloc byte[16];
        WriteRgb565(color0, colors, 0);
        WriteRgb565(color1, colors, 4);
        if (color0 > color1)
        {
            colors[8] = (byte)((2 * colors[0] + colors[4]) / 3);
            colors[9] = (byte)((2 * colors[1] + colors[5]) / 3);
            colors[10] = (byte)((2 * colors[2] + colors[6]) / 3);
            colors[11] = 255;
            colors[12] = (byte)((colors[0] + 2 * colors[4]) / 3);
            colors[13] = (byte)((colors[1] + 2 * colors[5]) / 3);
            colors[14] = (byte)((colors[2] + 2 * colors[6]) / 3);
            colors[15] = 255;
        }
        else
        {
            colors[8] = (byte)((colors[0] + colors[4]) / 2);
            colors[9] = (byte)((colors[1] + colors[5]) / 2);
            colors[10] = (byte)((colors[2] + colors[6]) / 2);
            colors[11] = 255;
            colors[12] = 0;
            colors[13] = 0;
            colors[14] = 0;
            colors[15] = 0;
        }

        for (var pixel = 0; pixel < 16; pixel++)
        {
            var localX = pixel & 3;
            var localY = pixel >> 2;
            var x = startX + localX;
            var y = startY + localY;
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                alphaBits >>= 3;
                colorBits >>= 2;
                continue;
            }

            var colorIndex = (int)(colorBits & 0x3);
            var alphaIndex = (int)(alphaBits & 0x7);
            colorBits >>= 2;
            alphaBits >>= 3;

            var rgbaIndex = ((y * width) + x) * 4;
            var colorBase = colorIndex * 4;
            targetRgba[rgbaIndex] = colors[colorBase];
            targetRgba[rgbaIndex + 1] = colors[colorBase + 1];
            targetRgba[rgbaIndex + 2] = colors[colorBase + 2];
            targetRgba[rgbaIndex + 3] = alphaPalette[alphaIndex];
        }
    }

    private static void CalculateAlphaStats(byte[] rgba, out byte alphaMin, out byte alphaMax, out int alphaNonZero)
    {
        alphaMin = 0;
        alphaMax = 0;
        alphaNonZero = 0;
        if (rgba.Length == 0)
        {
            return;
        }

        alphaMin = byte.MaxValue;
        for (var index = 3; index < rgba.Length; index += 4)
        {
            var alpha = rgba[index];
            if (alpha < alphaMin)
            {
                alphaMin = alpha;
            }

            if (alpha > alphaMax)
            {
                alphaMax = alpha;
            }

            if (alpha > 0)
            {
                alphaNonZero++;
            }
        }
    }

    private static void WriteRgb565(ushort value, Span<byte> target, int offset)
    {
        var r = (byte)((((value >> 11) & 0x1F) * 255 + 15) / 31);
        var g = (byte)((((value >> 5) & 0x3F) * 255 + 31) / 63);
        var b = (byte)(((value & 0x1F) * 255 + 15) / 31);
        target[offset] = r;
        target[offset + 1] = g;
        target[offset + 2] = b;
        target[offset + 3] = 255;
    }

    private static int GetLrlePixelRunLength(byte[] mip, ref int pointer)
    {
        var count = (mip[pointer] & 0x7f) >> 2;
        var shift = 5;
        while ((mip[pointer] & 0x80) != 0)
        {
            pointer++;
            EnsureReadCapacity(mip, pointer, 1);
            count += (mip[pointer] & 0x7f) << shift;
            shift += 7;
        }

        return count;
    }

    private static int GetLrleRepeatRunLength(byte[] mip, ref int pointer)
    {
        var count = (mip[pointer] & 0x7F) >> 3;
        var shift = 4;
        while ((mip[pointer] & 0x80) != 0)
        {
            pointer++;
            EnsureReadCapacity(mip, pointer, 1);
            count += (mip[pointer] & 0x7f) << shift;
            shift += 7;
        }

        return count;
    }

    private static int GetLrleColorIndex(byte[] mip, ref int pointer)
    {
        var count = mip[pointer] & 0x7f;
        var shift = 7;
        while ((mip[pointer] & 0x80) != 0)
        {
            pointer++;
            EnsureReadCapacity(mip, pointer, 1);
            count += (mip[pointer] & 0x7f) << shift;
            shift += 7;
        }

        return count;
    }

    private static byte[] ReadEmbeddedLrleRun(byte[] data, ref int pixelCount, ref int pointer)
    {
        var result = new byte[checked(pixelCount * 4)];
        var resultPointer = 0;
        while (resultPointer < result.Length)
        {
            EnsureReadCapacity(data, pointer, 1);
            var instruction = data[pointer];
            if ((instruction & 1) == 1)
            {
                var count = (instruction & 0x7F) >> 1;
                if ((instruction & 0x80) == 0x80)
                {
                    pointer++;
                    EnsureReadCapacity(data, pointer, 1);
                    count += data[pointer] << 6;
                }

                pointer++;
                EnsureReadCapacity(data, pointer, count);
                CopyBytes(data, ref pointer, result, ref resultPointer, count);
                continue;
            }

            if ((instruction & 2) == 2)
            {
                var count = (instruction & 0x7F) >> 2;
                if ((instruction & 0x80) == 0x80)
                {
                    pointer++;
                    EnsureReadCapacity(data, pointer, 1);
                    count += data[pointer] << 5;
                }

                pointer++;
                EnsureReadCapacity(data, pointer, 1);
                for (var i = 0; i < count; i++)
                {
                    EnsureWriteCapacity(result, resultPointer, 1);
                    result[resultPointer] = data[pointer];
                    resultPointer++;
                }

                pointer++;
                continue;
            }

            {
                var count = (instruction & 0x7F) >> 2;
                if ((instruction & 0x80) == 0x80)
                {
                    pointer++;
                    EnsureReadCapacity(data, pointer, 1);
                    count += data[pointer] << 5;
                }

                FillZeros(result, ref resultPointer, count);
                pointer++;
            }
        }

        return result;
    }

    private static byte[] RearrangeLrleBlocks(byte[] blockOrderPixels, int width, int height)
    {
        var output = new byte[checked(width * height * 4)];
        var x = 0;
        var y = 0;
        var rowStride = width * 4;
        for (var offset = 0; offset < blockOrderPixels.Length; offset += 64)
        {
            for (var row = 0; row < 4; row++)
            {
                var targetOffset = (y * rowStride) + x;
                EnsureWriteCapacity(output, targetOffset, 16);
                EnsureReadCapacity(blockOrderPixels, offset + (row * 16), 16);
                Array.Copy(blockOrderPixels, offset + (row * 16), output, targetOffset, 16);
                y++;
            }

            x += 16;
            if (x >= rowStride)
            {
                x = 0;
            }
            else
            {
                y -= 4;
            }
        }

        return output;
    }

    private static void EnsurePaletteIndex(IReadOnlyList<byte[]> palette, int index)
    {
        if ((uint)index >= (uint)palette.Count || palette[index].Length < 4)
        {
            throw new InvalidDataException($"Invalid LRLE palette index {index}.");
        }
    }

    private static void CopyBytes(byte[] source, ref int sourceOffset, byte[] destination, ref int destinationOffset, int count)
    {
        EnsureReadCapacity(source, sourceOffset, count);
        EnsureWriteCapacity(destination, destinationOffset, count);
        Array.Copy(source, sourceOffset, destination, destinationOffset, count);
        sourceOffset += count;
        destinationOffset += count;
    }

    private static void CopyBytes(byte[] source, int sourceOffset, byte[] destination, ref int destinationOffset, int count)
    {
        EnsureReadCapacity(source, sourceOffset, count);
        EnsureWriteCapacity(destination, destinationOffset, count);
        Array.Copy(source, sourceOffset, destination, destinationOffset, count);
        destinationOffset += count;
    }

    private static void FillZeros(byte[] destination, ref int destinationOffset, int count)
    {
        EnsureWriteCapacity(destination, destinationOffset, count);
        Array.Clear(destination, destinationOffset, count);
        destinationOffset += count;
    }

    private static void WriteBlock(byte[] destination, ref int destinationOffset, ReadOnlySpan<byte> block)
    {
        EnsureWriteCapacity(destination, destinationOffset, block.Length);
        block.CopyTo(destination.AsSpan(destinationOffset, block.Length));
        destinationOffset += block.Length;
    }

    private static void CopyBlock(byte[] source, ref int sourceOffset, byte[] destination, ref int destinationOffset, int count)
    {
        EnsureReadCapacity(source, sourceOffset, count);
        EnsureWriteCapacity(destination, destinationOffset, count);
        Array.Copy(source, sourceOffset, destination, destinationOffset, count);
        sourceOffset += count;
        destinationOffset += count;
    }

    private static void EnsureReadCapacity(byte[] data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
        {
            throw new EndOfStreamException("Unexpected end of stream.");
        }
    }

    private static void EnsureWriteCapacity(byte[] data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
        {
            throw new InvalidDataException("Decoded texture data exceeds expected output dimensions.");
        }
    }

    private static ReadOnlySpan<byte> FullTransparentAlpha => [0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
    private static ReadOnlySpan<byte> FullTransparentWhite => [0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
    private static ReadOnlySpan<byte> FullTransparentBlack => [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
    private static ReadOnlySpan<byte> FullOpaqueAlpha => [0x00, 0x05, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    private sealed class MipHeader
    {
        public int CommandOffset { get; init; }
        public int Offset0 { get; init; }
        public int Offset1 { get; init; }
        public int Offset2 { get; init; }
        public int Offset3 { get; init; }
        public int Offset4 { get; init; }
    }
}
