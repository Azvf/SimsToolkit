namespace SimsModDesktop.Application.TextureProcessing;

public interface ITextureTranscodePipeline
{
    TextureTranscodeResult Transcode(TextureTranscodeRequest request);
}
