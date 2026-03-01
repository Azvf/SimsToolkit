using System.Buffers.Binary;

namespace SimsModDesktop.Services;

internal static class TrayImagePayloadScanner
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> PngEndMarker => [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];
    private static ReadOnlySpan<byte> DdsSignature => [0x44, 0x44, 0x53, 0x20];

    public static ExtractedTrayImage? TryExtractBestImage(ReadOnlySpan<byte> data)
    {
        var structured = ExtractStructuredCandidates(data);
        var bestStructured = SelectBest(structured);
        if (bestStructured is not null)
        {
            return bestStructured;
        }

        return SelectBest(ExtractRawCandidates(data));
    }

    private static List<ExtractedTrayImage> ExtractStructuredCandidates(ReadOnlySpan<byte> data)
    {
        var candidates = new List<ExtractedTrayImage>();
        if (data.Length < 16)
        {
            return candidates;
        }

        for (var i = 0; i <= data.Length - 12; i++)
        {
            var candidateLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(i, 4));
            if (candidateLength < 16 || candidateLength > data.Length - i - 4)
            {
                continue;
            }

            if (TryCreateCandidate(data.Slice(i + 4, candidateLength), 0, candidateLength, out var image, out _))
            {
                candidates.Add(image!);
                i += candidateLength + 3;
            }
        }

        return candidates;
    }

    private static List<ExtractedTrayImage> ExtractRawCandidates(ReadOnlySpan<byte> data)
    {
        var candidates = new List<ExtractedTrayImage>();
        if (data.Length < 4)
        {
            return candidates;
        }

        for (var i = 0; i < data.Length - 2; i++)
        {
            if (!TryCreateCandidate(data, i, data.Length - i, out var image, out var consumed))
            {
                continue;
            }

            candidates.Add(image!);
            i += Math.Max(consumed - 1, 0);
        }

        return candidates;
    }

    private static ExtractedTrayImage? SelectBest(IEnumerable<ExtractedTrayImage> candidates)
    {
        ExtractedTrayImage? best = null;

        foreach (var candidate in candidates)
        {
            if (best is null || Compare(candidate, best) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static int Compare(ExtractedTrayImage left, ExtractedTrayImage right)
    {
        var leftAspect = Math.Abs(left.Width - left.Height) / (double)Math.Max(Math.Max(left.Width, left.Height), 1);
        var rightAspect = Math.Abs(right.Width - right.Height) / (double)Math.Max(Math.Max(right.Width, right.Height), 1);
        var aspectCompare = leftAspect.CompareTo(rightAspect);
        if (aspectCompare != 0)
        {
            return aspectCompare;
        }

        var areaCompare = right.PixelArea.CompareTo(left.PixelArea);
        if (areaCompare != 0)
        {
            return areaCompare;
        }

        return 0;
    }

    private static bool TryCreateCandidate(
        ReadOnlySpan<byte> data,
        int start,
        int maxLength,
        out ExtractedTrayImage? image,
        out int consumedLength)
    {
        image = null;
        consumedLength = 0;

        if (start < 0 || maxLength < 4 || start + 4 > data.Length)
        {
            return false;
        }

        if (HasSignature(data, start, PngSignature))
        {
            if (!TryFindPngLength(data, start, maxLength, out consumedLength))
            {
                return false;
            }

            return TryFinalizeCandidate(data.Slice(start, consumedLength), out image);
        }

        if (IsJpegStart(data, start))
        {
            if (!TryFindJpegLength(data, start, maxLength, out consumedLength))
            {
                return false;
            }

            return TryFinalizeCandidate(data.Slice(start, consumedLength), out image);
        }

        if (HasSignature(data, start, DdsSignature))
        {
            if (!TryFindDdsLength(data, start, maxLength, out consumedLength))
            {
                return false;
            }

            return TryFinalizeCandidate(data.Slice(start, consumedLength), out image);
        }

        return false;
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

    private static bool TryFindPngLength(ReadOnlySpan<byte> data, int start, int maxLength, out int length)
    {
        length = 0;
        var maxEnd = Math.Min(data.Length, start + maxLength);
        var marker = PngEndMarker;

        for (var i = start + PngSignature.Length; i <= maxEnd - marker.Length; i++)
        {
            if (!HasSignature(data, i, marker))
            {
                continue;
            }

            length = (i - start) + marker.Length;
            return true;
        }

        return false;
    }

    private static bool TryFindJpegLength(ReadOnlySpan<byte> data, int start, int maxLength, out int length)
    {
        length = 0;
        var maxEnd = Math.Min(data.Length, start + maxLength);

        for (var i = start + 2; i < maxEnd - 1; i++)
        {
            if (data[i] != 0xFF || data[i + 1] != 0xD9)
            {
                continue;
            }

            length = (i - start) + 2;
            return true;
        }

        return false;
    }

    private static bool TryFindDdsLength(ReadOnlySpan<byte> data, int start, int maxLength, out int length)
    {
        length = 0;
        var maxEnd = Math.Min(data.Length, start + maxLength);

        if (maxEnd - start < 128)
        {
            return false;
        }

        for (var i = start + 128; i < maxEnd - 4; i++)
        {
            if (!HasSignature(data, i, PngSignature) &&
                !IsJpegStart(data, i) &&
                !HasSignature(data, i, DdsSignature))
            {
                continue;
            }

            length = i - start;
            return length > 128;
        }

        length = maxEnd - start;
        return length > 128;
    }

    private static bool IsJpegStart(ReadOnlySpan<byte> data, int index)
    {
        return index + 2 < data.Length &&
               data[index] == 0xFF &&
               data[index + 1] == 0xD8 &&
               data[index + 2] == 0xFF;
    }

    private static bool HasSignature(ReadOnlySpan<byte> data, int index, ReadOnlySpan<byte> signature)
    {
        return index >= 0 &&
               index + signature.Length <= data.Length &&
               data.Slice(index, signature.Length).SequenceEqual(signature);
    }
}
