namespace SimsModDesktop.Application.Saves;

public interface ISavePreviewCacheStore
{
    string GetCacheRootPath(string saveFilePath);
    bool IsCurrent(string saveFilePath, SavePreviewCacheManifest manifest);
    bool TryLoad(string saveFilePath, out SavePreviewCacheManifest manifest);
    void Save(string saveFilePath, SavePreviewCacheManifest manifest);
    void Clear(string saveFilePath);
}
