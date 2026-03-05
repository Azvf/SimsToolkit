using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.TextureProcessing;

namespace SimsModDesktop.Infrastructure.TextureCompression;

public sealed class TextureCompressionService : ITextureCompressionService
{
    private readonly ITextureDecodeService _decodeService;
    private readonly ITextureTranscodePipeline _pipeline;
    private readonly ILogger<TextureCompressionService> _logger;

    public TextureCompressionService(
        ITextureDecodeService decodeService,
        ITextureTranscodePipeline pipeline,
        ILogger<TextureCompressionService>? logger = null)
    {
        _decodeService = decodeService;
        _pipeline = pipeline;
        _logger = logger ?? NullLogger<TextureCompressionService>.Instance;
    }

    public TextureCompressionResult Compress(TextureCompressionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selectedFormat = request.PreferredFormat ?? SelectDefaultFormat(request.Source);
        var targetWidth = request.TargetWidth;
        var targetHeight = request.TargetHeight;

        if (!targetWidth.HasValue || !targetHeight.HasValue)
        {
            if (!_decodeService.TryDecode(request.Source.ContainerKind, request.SourceBytes, out var decoded, out var decodeError))
            {
                _logger.LogError(
                    "{Event} status={Status} domain={Domain} container={Container} error={Error}",
                    "texture.compress.decode.fail",
                    "fail",
                    "texture",
                    request.Source.ContainerKind,
                    decodeError ?? string.Empty);
                return new TextureCompressionResult
                {
                    Success = false,
                    SelectedFormat = selectedFormat,
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
            targetWidth ??= decoded.Width;
            targetHeight ??= decoded.Height;
        }

        if (targetWidth < 1 || targetHeight < 1)
        {
            return new TextureCompressionResult
            {
                Success = false,
                SelectedFormat = selectedFormat,
                Error = "Target dimensions must be greater than zero."
            };
        }

        if (request.Source.SourcePixelFormat is TexturePixelFormatKind.Bc7 or TexturePixelFormatKind.Unknown)
        {
            return new TextureCompressionResult
            {
                Success = false,
                SelectedFormat = selectedFormat,
                Error = $"Source pixel format is not supported for automatic compression: {request.Source.SourcePixelFormat}."
            };
        }

        var result = _pipeline.Transcode(new TextureTranscodeRequest
        {
            Source = request.Source,
            SourceBytes = request.SourceBytes,
            TargetFormat = selectedFormat,
            TargetWidth = targetWidth.Value,
            TargetHeight = targetHeight.Value,
            GenerateMipMaps = request.GenerateMipMaps,
            ColorSpaceHint = request.ColorSpaceHint
        });

        return new TextureCompressionResult
        {
            Success = result.Success,
            SelectedFormat = selectedFormat,
            TranscodeResult = result,
            Error = result.Error
        };
    }

    private static TextureTargetFormat SelectDefaultFormat(TextureSourceDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.HasAlpha ? TextureTargetFormat.Bc3 : TextureTargetFormat.Bc1;
    }
}
