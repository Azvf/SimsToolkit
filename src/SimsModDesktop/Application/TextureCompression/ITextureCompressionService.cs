namespace SimsModDesktop.Application.TextureCompression;

public interface ITextureCompressionService
{
    TextureCompressionResult Compress(TextureCompressionRequest request);
}
