namespace SimsModDesktop.Application.Saves;

public interface ILoadSaveWithAppearanceLinksService
{
    Task<LoadSaveWithAppearanceLinksResult> LoadAsync(
        LoadSaveWithAppearanceLinksRequest request,
        CancellationToken cancellationToken = default);
}
