using SkiaSharp;

namespace SimsModDesktop.Services;

internal static class TrayImageCodec
{
    public static bool TryMeasure(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.IsEmpty)
        {
            return false;
        }

        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(data.ToArray()));
            if (codec is null)
            {
                return false;
            }

            width = codec.Info.Width;
            height = codec.Info.Height;
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryTranscodeToCanvasPng(
        ReadOnlySpan<byte> data,
        int targetWidth,
        int targetHeight,
        out byte[] pngBytes,
        out int sourceWidth,
        out int sourceHeight)
    {
        pngBytes = Array.Empty<byte>();
        sourceWidth = 0;
        sourceHeight = 0;

        if (data.IsEmpty || targetWidth < 1 || targetHeight < 1)
        {
            return false;
        }

        try
        {
            using var sourceBitmap = SKBitmap.Decode(data.ToArray());
            if (sourceBitmap is null || sourceBitmap.Width < 1 || sourceBitmap.Height < 1)
            {
                return false;
            }

            sourceWidth = sourceBitmap.Width;
            sourceHeight = sourceBitmap.Height;

            using var surface = SKSurface.Create(new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (surface is null)
            {
                return false;
            }

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            var scale = Math.Min(
                targetWidth / (float)sourceBitmap.Width,
                targetHeight / (float)sourceBitmap.Height);

            var scaledWidth = sourceBitmap.Width * scale;
            var scaledHeight = sourceBitmap.Height * scale;
            var offsetX = (targetWidth - scaledWidth) / 2f;
            var offsetY = (targetHeight - scaledHeight) / 2f;
            var destinationRect = new SKRect(offsetX, offsetY, offsetX + scaledWidth, offsetY + scaledHeight);

            using var paint = new SKPaint
            {
                FilterQuality = SKFilterQuality.High,
                IsAntialias = true
            };

            canvas.DrawBitmap(sourceBitmap, destinationRect, paint);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded is null)
            {
                return false;
            }

            pngBytes = encoded.ToArray();
            return pngBytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
