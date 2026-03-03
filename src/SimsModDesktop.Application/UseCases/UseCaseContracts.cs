namespace SimsModDesktop.Application.UseCases;

public interface IUseCase<in TRequest, TResult>
{
    Task<TResult> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);
}

public sealed record OrganizeModsRequest;
public sealed record OrganizeModsResult;
public sealed record FlattenModsRequest;
public sealed record FlattenModsResult;
public sealed record NormalizeNamesRequest;
public sealed record NormalizeNamesResult;
public sealed record MergeFoldersRequest;
public sealed record MergeFoldersResult;
public sealed record FindDuplicateFilesRequest;
public sealed record FindDuplicateFilesResult;
public sealed record CompressTexturesRequest;
public sealed record CompressTexturesResult;
public sealed record LoadTrayPreviewRequest;
public sealed record LoadTrayPreviewResult;
public sealed record LoadTrayPreviewPageRequest;
public sealed record LoadTrayPreviewPageResult;
public sealed record ExportTraySelectionRequest;
public sealed record ExportTraySelectionResult;
public sealed record AnalyzeTrayDependenciesRequest;
public sealed record AnalyzeTrayDependenciesResult;
public sealed record LoadModCatalogRequest;
public sealed record LoadModCatalogResult;
public sealed record InspectModItemRequest;
public sealed record InspectModItemResult;
public sealed record LoadSaveCatalogRequest;
public sealed record LoadSaveCatalogResult;
public sealed record LaunchGameRequest;
public sealed record LaunchGameResult;

public interface IOrganizeModsUseCase : IUseCase<OrganizeModsRequest, OrganizeModsResult>;
public interface IFlattenModsUseCase : IUseCase<FlattenModsRequest, FlattenModsResult>;
public interface INormalizeNamesUseCase : IUseCase<NormalizeNamesRequest, NormalizeNamesResult>;
public interface IMergeFoldersUseCase : IUseCase<MergeFoldersRequest, MergeFoldersResult>;
public interface IFindDuplicateFilesUseCase : IUseCase<FindDuplicateFilesRequest, FindDuplicateFilesResult>;
public interface ICompressTexturesUseCase : IUseCase<CompressTexturesRequest, CompressTexturesResult>;
public interface ILoadTrayPreviewUseCase : IUseCase<LoadTrayPreviewRequest, LoadTrayPreviewResult>;
public interface ILoadTrayPreviewPageUseCase : IUseCase<LoadTrayPreviewPageRequest, LoadTrayPreviewPageResult>;
public interface IExportTraySelectionUseCase : IUseCase<ExportTraySelectionRequest, ExportTraySelectionResult>;
public interface IAnalyzeTrayDependenciesUseCase : IUseCase<AnalyzeTrayDependenciesRequest, AnalyzeTrayDependenciesResult>;
public interface ILoadModCatalogUseCase : IUseCase<LoadModCatalogRequest, LoadModCatalogResult>;
public interface IInspectModItemUseCase : IUseCase<InspectModItemRequest, InspectModItemResult>;
public interface ILoadSaveCatalogUseCase : IUseCase<LoadSaveCatalogRequest, LoadSaveCatalogResult>;
public interface ILaunchGameUseCase : IUseCase<LaunchGameRequest, LaunchGameResult>;
