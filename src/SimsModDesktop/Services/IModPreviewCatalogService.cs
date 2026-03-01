using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public interface IModPreviewCatalogService
{
    Task<IReadOnlyList<ModPreviewCatalogItem>> LoadCatalogAsync(
        ModPreviewCatalogQuery query,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ModPreviewCatalogPage> StreamCatalogAsync(
        ModPreviewCatalogQuery query,
        CancellationToken cancellationToken = default);
}
