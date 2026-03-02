namespace SimsModDesktop.Infrastructure.TextureProcessing;

public sealed class TextureTranscodePipeline : ITextureTranscodePipeline
{
    private readonly ITextureDecodeService _decoder;
    private readonly ITextureResizeService _resizeService;
    private readonly ITextureEncodeService _encoder;

    public TextureTranscodePipeline(
        ITextureDecodeService decoder,
        ITextureResizeService resizeService,
        ITextureEncodeService encoder)
    {
        _decoder = decoder;
        _resizeService = resizeService;
        _encoder = encoder;
    }

    public TextureTranscodeResult Transcode(TextureTranscodeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TargetWidth < 1 || request.TargetHeight < 1)
        {
            return new TextureTranscodeResult
            {
                Success = false,
                OutputFormat = request.TargetFormat,
                Error = "Target dimensions must be greater than zero."
            };
        }

        if (!IsBlockAligned(request.TargetWidth, request.TargetHeight))
        {
            return new TextureTranscodeResult
            {
                Success = false,
                OutputFormat = request.TargetFormat,
                Error = "BC textures require width and height to be multiples of 4."
            };
        }

        if (!_decoder.TryDecode(request.Source.ContainerKind, request.SourceBytes, out var decoded, out var decodeError))
        {
            return new TextureTranscodeResult
            {
                Success = false,
                OutputFormat = request.TargetFormat,
                Error = decodeError
            };
        }

        TexturePixelBuffer workingBuffer;
        try
        {
            workingBuffer = decoded.Width == request.TargetWidth && decoded.Height == request.TargetHeight
                ? decoded
                : _resizeService.Resize(decoded, request.TargetWidth, request.TargetHeight);
        }
        catch (Exception ex)
        {
            return new TextureTranscodeResult
            {
                Success = false,
                OutputFormat = request.TargetFormat,
                Error = $"Failed to resize texture: {ex.Message}"
            };
        }

        if (!_encoder.TryEncode(
                workingBuffer,
                request.TargetFormat,
                request.GenerateMipMaps,
                out var encodedBytes,
                out var mipMapCount,
                out var encodeError))
        {
            return new TextureTranscodeResult
            {
                Success = false,
                OutputFormat = request.TargetFormat,
                Error = encodeError
            };
        }

        return new TextureTranscodeResult
        {
            Success = true,
            EncodedBytes = encodedBytes,
            OutputFormat = request.TargetFormat,
            OutputWidth = workingBuffer.Width,
            OutputHeight = workingBuffer.Height,
            MipMapCount = mipMapCount
        };
    }

    private static bool IsBlockAligned(int width, int height)
    {
        return width % 4 == 0 && height % 4 == 0;
    }
}
