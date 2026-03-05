using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SimsModDesktop.Infrastructure.TextureProcessing;

public sealed class TextureTranscodePipeline : ITextureTranscodePipeline
{
    private readonly ITextureDecodeService _decoder;
    private readonly ITextureResizeService _resizeService;
    private readonly ITextureEncodeService _encoder;
    private readonly ILogger<TextureTranscodePipeline> _logger;

    public TextureTranscodePipeline(
        ITextureDecodeService decoder,
        ITextureResizeService resizeService,
        ITextureEncodeService encoder,
        ILogger<TextureTranscodePipeline>? logger = null)
    {
        _decoder = decoder;
        _resizeService = resizeService;
        _encoder = encoder;
        _logger = logger ?? NullLogger<TextureTranscodePipeline>.Instance;
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

        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} container={Container} sourceBytes={SourceBytes} targetWidth={TargetWidth} targetHeight={TargetHeight} targetFormat={TargetFormat}",
            "texture.compress.decode.start",
            "start",
            "texture",
            request.Source.ContainerKind,
            request.SourceBytes.Length,
            request.TargetWidth,
            request.TargetHeight,
            request.TargetFormat);
        if (!_decoder.TryDecode(request.Source.ContainerKind, request.SourceBytes, out var decoded, out var decodeError))
        {
            _logger.LogError(
                "{Event} status={Status} domain={Domain} container={Container} error={Error}",
                "texture.compress.decode.fail",
                "fail",
                "texture",
                request.Source.ContainerKind,
                decodeError ?? string.Empty);
            return new TextureTranscodeResult
            {
                Success = false,
                OutputFormat = request.TargetFormat,
                Error = decodeError
            };
        }
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} container={Container} width={Width} height={Height}",
            "texture.compress.decode.done",
            "done",
            "texture",
            request.Source.ContainerKind,
            decoded.Width,
            decoded.Height);

        TexturePixelBuffer workingBuffer;
        try
        {
            _logger.LogInformation(
                "{Event} status={Status} domain={Domain} sourceWidth={SourceWidth} sourceHeight={SourceHeight} targetWidth={TargetWidth} targetHeight={TargetHeight} skipped={Skipped}",
                "texture.compress.resize.start",
                "start",
                "texture",
                decoded.Width,
                decoded.Height,
                request.TargetWidth,
                request.TargetHeight,
                decoded.Width == request.TargetWidth && decoded.Height == request.TargetHeight);
            workingBuffer = decoded.Width == request.TargetWidth && decoded.Height == request.TargetHeight
                ? decoded
                : _resizeService.Resize(decoded, request.TargetWidth, request.TargetHeight);
            _logger.LogInformation(
                "{Event} status={Status} domain={Domain} outputWidth={OutputWidth} outputHeight={OutputHeight}",
                "texture.compress.resize.done",
                "done",
                "texture",
                workingBuffer.Width,
                workingBuffer.Height);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{Event} status={Status} domain={Domain} sourceWidth={SourceWidth} sourceHeight={SourceHeight} targetWidth={TargetWidth} targetHeight={TargetHeight}",
                "texture.compress.resize.fail",
                "fail",
                "texture",
                decoded.Width,
                decoded.Height,
                request.TargetWidth,
                request.TargetHeight);
            return new TextureTranscodeResult
            {
                Success = false,
                OutputFormat = request.TargetFormat,
                Error = $"Failed to resize texture: {ex.Message}"
            };
        }

        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} targetFormat={TargetFormat} mipmaps={GenerateMipmaps} width={Width} height={Height}",
            "texture.compress.encode.start",
            "start",
            "texture",
            request.TargetFormat,
            request.GenerateMipMaps,
            workingBuffer.Width,
            workingBuffer.Height);
        if (!_encoder.TryEncode(
                workingBuffer,
                request.TargetFormat,
                request.GenerateMipMaps,
                out var encodedBytes,
                out var mipMapCount,
                out var encodeError))
        {
            _logger.LogError(
                "{Event} status={Status} domain={Domain} targetFormat={TargetFormat} error={Error}",
                "texture.compress.encode.fail",
                "fail",
                "texture",
                request.TargetFormat,
                encodeError ?? string.Empty);
            return new TextureTranscodeResult
            {
                Success = false,
                OutputFormat = request.TargetFormat,
                Error = encodeError
            };
        }
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} targetFormat={TargetFormat} encodedBytes={EncodedBytes} mipMapCount={MipMapCount}",
            "texture.compress.encode.done",
            "done",
            "texture",
            request.TargetFormat,
            encodedBytes.Length,
            mipMapCount);

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
