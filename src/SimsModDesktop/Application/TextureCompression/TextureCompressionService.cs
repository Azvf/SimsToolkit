using SimsModDesktop.Infrastructure.TextureProcessing;

namespace SimsModDesktop.Application.TextureCompression;

public sealed class TextureCompressionService : ITextureCompressionService
{
    private readonly ITextureDecodeService _decodeService;
    private readonly ITextureTranscodePipeline _pipeline;

    public TextureCompressionService(
        ITextureDecodeService decodeService,
        ITextureTranscodePipeline pipeline)
    {
        _decodeService = decodeService;
        _pipeline = pipeline;
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
                return new TextureCompressionResult
                {
                    Success = false,
                    SelectedFormat = selectedFormat,
                    Error = decodeError
                };
            }

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
