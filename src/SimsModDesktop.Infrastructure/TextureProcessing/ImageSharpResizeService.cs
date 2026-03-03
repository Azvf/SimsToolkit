using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SimsModDesktop.Infrastructure.TextureProcessing;

public sealed class ImageSharpResizeService : ITextureResizeService
{
    public TexturePixelBuffer Resize(TexturePixelBuffer source, int targetWidth, int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Validate();

        if (targetWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Target width must be greater than zero.");
        }

        if (targetHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetHeight), "Target height must be greater than zero.");
        }

        if (source.Width == targetWidth && source.Height == targetHeight)
        {
            return new TexturePixelBuffer
            {
                Width = source.Width,
                Height = source.Height,
                Layout = source.Layout,
                PixelBytes = source.PixelBytes.ToArray()
            };
        }

        using var image = Image.LoadPixelData<Rgba32>(source.PixelBytes, source.Width, source.Height);
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(targetWidth, targetHeight),
            Sampler = KnownResamplers.Lanczos3,
            Mode = ResizeMode.Stretch
        }));

        var pixels = new byte[checked(targetWidth * targetHeight * 4)];
        image.CopyPixelDataTo(pixels);

        var resized = new TexturePixelBuffer
        {
            Width = targetWidth,
            Height = targetHeight,
            Layout = TexturePixelLayout.Rgba32,
            PixelBytes = pixels
        };
        resized.Validate();
        return resized;
    }
}
