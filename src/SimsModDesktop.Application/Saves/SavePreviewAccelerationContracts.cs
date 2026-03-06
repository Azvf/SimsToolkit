namespace SimsModDesktop.Application.Saves;

public interface ISavePreviewDescriptorStore
{
    bool IsDescriptorCurrent(string saveFilePath, SavePreviewDescriptorManifest manifest);

    bool TryLoadDescriptor(string saveFilePath, out SavePreviewDescriptorManifest manifest);

    void SaveDescriptor(string saveFilePath, SavePreviewDescriptorManifest manifest);

    void ClearDescriptor(string saveFilePath);
}

public interface ISavePreviewArtifactProvider
{
    Task<string?> EnsureBundleAsync(
        string saveFilePath,
        string householdKey,
        string purpose,
        CancellationToken cancellationToken = default);

    void Clear(string saveFilePath);
}
