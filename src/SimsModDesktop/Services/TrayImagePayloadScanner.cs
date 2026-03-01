using System.Buffers.Binary;

namespace SimsModDesktop.Services;

internal static class TrayImagePayloadScanner
{
    private const int EncodedContainerHeaderSize = 24;
    private const int MaxEncodedPayloadBytes = 32 * 1024 * 1024;
    private const int EmbeddedAlphaHeaderBytes = 8;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> DdsSignature => [0x44, 0x44, 0x53, 0x20];
    private static ReadOnlySpan<byte> XorKey => [0x41, 0x25, 0xE6, 0xCD, 0x47, 0xBA, 0xB2, 0x1A];
    private static ReadOnlySpan<byte> AlfaTag => [0x41, 0x4C, 0x46, 0x41];
    private static ReadOnlySpan<byte> AlfjTag => [0x41, 0x4C, 0x46, 0x4A];

    public static ExtractedTrayImage? TryExtractBestImage(ReadOnlySpan<byte> data)
    {
        if (TryExtractDirectImage(data, out var image))
        {
            return image;
        }

        if (TryExtractEncodedContainerImage(data, out image))
        {
            return image;
        }

        return null;
    }

    private static bool TryExtractDirectImage(ReadOnlySpan<byte> data, out ExtractedTrayImage? image)
    {
        image = null;

        if (!HasDirectImageSignature(data))
        {
            return false;
        }

        return TryFinalizeCandidate(data, out image);
    }

    private static bool TryExtractEncodedContainerImage(ReadOnlySpan<byte> data, out ExtractedTrayImage? image)
    {
        image = null;

        if (data.Length < EncodedContainerHeaderSize)
        {
            return false;
        }

        var encodedPayloadLengthValue = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        if (encodedPayloadLengthValue == 0 || encodedPayloadLengthValue > int.MaxValue)
        {
            return false;
        }

        var encodedPayloadLength = (int)encodedPayloadLengthValue;
        if (encodedPayloadLength > MaxEncodedPayloadBytes ||
            EncodedContainerHeaderSize + encodedPayloadLength > data.Length)
        {
            return false;
        }

        var decoded = DecodePayload(data.Slice(EncodedContainerHeaderSize, encodedPayloadLength));
        if (!IsJpegStart(decoded))
        {
            return false;
        }

        if (TryExtractEmbeddedAlphaImage(decoded, out image))
        {
            return true;
        }

        return TryFinalizeCandidate(decoded, out image);
    }

    private static byte[] DecodePayload(ReadOnlySpan<byte> encoded)
    {
        var decoded = new byte[encoded.Length];
        var key = XorKey;

        for (var i = 0; i < encoded.Length; i++)
        {
            decoded[i] = (byte)(encoded[i] ^ key[i % key.Length]);
        }

        return decoded;
    }

    private static bool TryExtractEmbeddedAlphaImage(ReadOnlySpan<byte> jpegWithAlpha, out ExtractedTrayImage? image)
    {
        image = null;

        if (!TryExtractNormalizedJpegAndAlphaMask(jpegWithAlpha, out var normalizedJpeg, out var alphaMaskBytes))
        {
            return false;
        }

        if (alphaMaskBytes is not null &&
            TrayImageCodec.TryApplyAlphaMask(
                normalizedJpeg,
                alphaMaskBytes,
                out var pngBytes,
                out var width,
                out var height))
        {
            image = new ExtractedTrayImage
            {
                Data = pngBytes,
                Width = width,
                Height = height
            };
            return true;
        }

        return TryFinalizeCandidate(normalizedJpeg, out image);
    }

    private static bool TryExtractNormalizedJpegAndAlphaMask(
        ReadOnlySpan<byte> jpegWithAlpha,
        out byte[] normalizedJpeg,
        out byte[]? alphaMaskBytes)
    {
        normalizedJpeg = Array.Empty<byte>();
        alphaMaskBytes = null;

        if (!IsJpegStart(jpegWithAlpha) || jpegWithAlpha.Length < 4)
        {
            return false;
        }

        var normalized = new List<byte>(jpegWithAlpha.Length)
        {
            jpegWithAlpha[0],
            jpegWithAlpha[1]
        };

        var foundEmbeddedAlpha = false;
        var position = 2;

        while (position < jpegWithAlpha.Length)
        {
            if (jpegWithAlpha[position] != 0xFF)
            {
                return false;
            }

            var markerStart = position;
            while (position < jpegWithAlpha.Length && jpegWithAlpha[position] == 0xFF)
            {
                position++;
            }

            if (position >= jpegWithAlpha.Length)
            {
                return false;
            }

            var marker = jpegWithAlpha[position++];
            if (marker == 0xD9)
            {
                normalized.AddRange(jpegWithAlpha.Slice(markerStart, position - markerStart).ToArray());
                normalizedJpeg = normalized.ToArray();
                return foundEmbeddedAlpha;
            }

            if (IsStandaloneMarker(marker))
            {
                normalized.AddRange(jpegWithAlpha.Slice(markerStart, position - markerStart).ToArray());
                continue;
            }

            if (position + 2 > jpegWithAlpha.Length)
            {
                return false;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(jpegWithAlpha.Slice(position, 2));
            if (segmentLength < 2 || position + segmentLength > jpegWithAlpha.Length)
            {
                return false;
            }

            if (marker == 0xDA)
            {
                normalized.AddRange(jpegWithAlpha[markerStart..].ToArray());
                normalizedJpeg = normalized.ToArray();
                return foundEmbeddedAlpha;
            }

            var payload = jpegWithAlpha.Slice(position + 2, segmentLength - 2);
            var isEmbeddedAlpha = marker == 0xE0 &&
                                  (payload.StartsWith(AlfaTag) || payload.StartsWith(AlfjTag));

            if (isEmbeddedAlpha)
            {
                if (foundEmbeddedAlpha)
                {
                    return false;
                }

                foundEmbeddedAlpha = true;
                alphaMaskBytes = TryReadEmbeddedAlphaMask(payload);
            }
            else
            {
                normalized.AddRange(
                    jpegWithAlpha.Slice(markerStart, (position - markerStart) + segmentLength).ToArray());
            }

            position += segmentLength;
        }

        return false;
    }

    private static byte[]? TryReadEmbeddedAlphaMask(ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= EmbeddedAlphaHeaderBytes)
        {
            return null;
        }

        var alphaLengthValue = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (alphaLengthValue == 0 || alphaLengthValue > int.MaxValue)
        {
            return null;
        }

        var alphaLength = (int)alphaLengthValue;
        if (alphaLength > MaxEncodedPayloadBytes || payload.Length != EmbeddedAlphaHeaderBytes + alphaLength)
        {
            return null;
        }

        return payload.Slice(EmbeddedAlphaHeaderBytes, alphaLength).ToArray();
    }

    private static bool TryFinalizeCandidate(ReadOnlySpan<byte> payload, out ExtractedTrayImage? image)
    {
        image = null;

        if (!TrayImageCodec.TryMeasure(payload, out var width, out var height))
        {
            return false;
        }

        image = new ExtractedTrayImage
        {
            Data = payload.ToArray(),
            Width = width,
            Height = height
        };
        return true;
    }

    private static bool HasDirectImageSignature(ReadOnlySpan<byte> data)
    {
        return data.StartsWith(PngSignature) ||
               data.StartsWith(DdsSignature) ||
               IsJpegStart(data);
    }

    private static bool IsJpegStart(ReadOnlySpan<byte> data)
    {
        return data.Length >= 4 &&
               data[0] == 0xFF &&
               data[1] == 0xD8 &&
               data[2] == 0xFF;
    }

    private static bool IsStandaloneMarker(byte marker)
    {
        return marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7);
    }
}
