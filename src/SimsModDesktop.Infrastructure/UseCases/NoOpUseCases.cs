using SimsModDesktop.Application.UseCases;

namespace SimsModDesktop.Infrastructure.UseCases;

public sealed class NoOpOrganizeModsUseCase : IOrganizeModsUseCase
{
    public Task<OrganizeModsResult> ExecuteAsync(OrganizeModsRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new OrganizeModsResult());
}

public sealed class NoOpFlattenModsUseCase : IFlattenModsUseCase
{
    public Task<FlattenModsResult> ExecuteAsync(FlattenModsRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new FlattenModsResult());
}

public sealed class NoOpNormalizeNamesUseCase : INormalizeNamesUseCase
{
    public Task<NormalizeNamesResult> ExecuteAsync(NormalizeNamesRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new NormalizeNamesResult());
}

public sealed class NoOpMergeFoldersUseCase : IMergeFoldersUseCase
{
    public Task<MergeFoldersResult> ExecuteAsync(MergeFoldersRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new MergeFoldersResult());
}

public sealed class NoOpFindDuplicateFilesUseCase : IFindDuplicateFilesUseCase
{
    public Task<FindDuplicateFilesResult> ExecuteAsync(FindDuplicateFilesRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new FindDuplicateFilesResult());
}

public sealed class NoOpCompressTexturesUseCase : ICompressTexturesUseCase
{
    public Task<CompressTexturesResult> ExecuteAsync(CompressTexturesRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new CompressTexturesResult());
}

public sealed class NoOpLoadTrayPreviewUseCase : ILoadTrayPreviewUseCase
{
    public Task<LoadTrayPreviewResult> ExecuteAsync(LoadTrayPreviewRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new LoadTrayPreviewResult());
}

public sealed class NoOpLoadTrayPreviewPageUseCase : ILoadTrayPreviewPageUseCase
{
    public Task<LoadTrayPreviewPageResult> ExecuteAsync(LoadTrayPreviewPageRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new LoadTrayPreviewPageResult());
}

public sealed class NoOpExportTraySelectionUseCase : IExportTraySelectionUseCase
{
    public Task<ExportTraySelectionResult> ExecuteAsync(ExportTraySelectionRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new ExportTraySelectionResult());
}

public sealed class NoOpAnalyzeTrayDependenciesUseCase : IAnalyzeTrayDependenciesUseCase
{
    public Task<AnalyzeTrayDependenciesResult> ExecuteAsync(AnalyzeTrayDependenciesRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AnalyzeTrayDependenciesResult());
}

public sealed class NoOpLoadModCatalogUseCase : ILoadModCatalogUseCase
{
    public Task<LoadModCatalogResult> ExecuteAsync(LoadModCatalogRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new LoadModCatalogResult());
}

public sealed class NoOpInspectModItemUseCase : IInspectModItemUseCase
{
    public Task<InspectModItemResult> ExecuteAsync(InspectModItemRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new InspectModItemResult());
}

public sealed class NoOpLoadSaveCatalogUseCase : ILoadSaveCatalogUseCase
{
    public Task<LoadSaveCatalogResult> ExecuteAsync(LoadSaveCatalogRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new LoadSaveCatalogResult());
}

public sealed class NoOpLaunchGameUseCase : ILaunchGameUseCase
{
    public Task<LaunchGameResult> ExecuteAsync(LaunchGameRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new LaunchGameResult());
}
