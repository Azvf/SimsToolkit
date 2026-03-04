using Pfim;
using SixLabors.ImageSharp;
using SimsModDesktop.Application.TextureProcessing;

namespace SimsModDesktop.Infrastructure.TextureProcessing;

public sealed class TextureDimensionProbe : ITextureDimensionProbe
{
    public bool TryGetDimensions(
        TextureContainerKind containerKind,
        ReadOnlyMemory<byte> sourceBytes,
        out int width,
        out int height,
        out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;

        if (sourceBytes.IsEmpty)
        {
            error = "Source bytes are empty.";
            return false;
        }

        try
        {
            switch (containerKind)
            {
                case TextureContainerKind.Png:
                {
                    var info = Image.Identify(sourceBytes.Span);
                    if (info is null)
                    {
                        error = "Failed to identify PNG dimensions.";
                        return false;
                    }

                    width = info.Width;
                    height = info.Height;
                    return width > 0 && height > 0;
                }
                case TextureContainerKind.Dds:
                case TextureContainerKind.Tga:
                {
                    using var stream = OpenReadOnlyMemoryStream(sourceBytes);
                    using var image = Pfimage.FromStream(stream);
                    width = image.Width;
                    height = image.Height;
                    return width > 0 && height > 0;
                }
                default:
                    error = $"Unsupported container kind: {containerKind}.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            error = $"Failed to probe texture dimensions: {ex.Message}";
            return false;
        }
    }

    private static MemoryStream OpenReadOnlyMemoryStream(ReadOnlyMemory<byte> sourceBytes)
    {
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(sourceBytes, out var segment) &&
            segment.Array is not null)
        {
            return new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false, publiclyVisible: true);
        }

        return new MemoryStream(sourceBytes.ToArray(), writable: false);
    }
}
