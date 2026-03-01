using System.Buffers.Binary;
using SkiaSharp;

namespace SimsModDesktop.Tests;

internal static class ImageTestHelpers
{
    private static ReadOnlySpan<byte> XorKey => [0x41, 0x25, 0xE6, 0xCD, 0x47, 0xBA, 0xB2, 0x1A];
    private static ReadOnlySpan<byte> AlfaTag => [0x41, 0x4C, 0x46, 0x41];

    public static byte[] CreatePngBytes(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(64, 128, 255, 255));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static byte[] CreateJpegBytes(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(64, 128, 255, 255));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    public static byte[] CreateAlphaMaskPngBytes(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = x < width / 2 ? (byte)0 : (byte)255;
                bitmap.SetPixel(x, y, new SKColor(alpha, alpha, alpha, 255));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static byte[] CreateJpegWithEmbeddedAlphaSegment(byte[] jpegBytes, byte[] alphaMaskBytes)
    {
        ArgumentNullException.ThrowIfNull(jpegBytes);
        ArgumentNullException.ThrowIfNull(alphaMaskBytes);

        var segmentLength = 2 + 4 + 4 + alphaMaskBytes.Length;
        var result = new byte[jpegBytes.Length + 2 + 2 + 4 + 4 + alphaMaskBytes.Length];

        result[0] = jpegBytes[0];
        result[1] = jpegBytes[1];
        result[2] = 0xFF;
        result[3] = 0xE0;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4, 2), checked((ushort)segmentLength));
        AlfaTag.CopyTo(result.AsSpan(6, 4));
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(10, 4), (uint)alphaMaskBytes.Length);
        alphaMaskBytes.CopyTo(result.AsSpan(14, alphaMaskBytes.Length));
        jpegBytes.AsSpan(2).CopyTo(result.AsSpan(14 + alphaMaskBytes.Length));

        return result;
    }

    public static byte[] CreateEncodedTrayImageBytes(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var result = new byte[24 + imageBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), (uint)imageBytes.Length);

        var key = XorKey;
        for (var i = 0; i < imageBytes.Length; i++)
        {
            result[24 + i] = (byte)(imageBytes[i] ^ key[i % key.Length]);
        }

        return result;
    }
}
