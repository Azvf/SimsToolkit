namespace SimsModDesktop.Infrastructure.TextureProcessing;

public interface ITextureTranscodePipeline
{
    TextureTranscodeResult Transcode(TextureTranscodeRequest request);
}
