using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Services;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Application.UseCases;
using SimsModDesktop.Infrastructure.Configuration;
using SimsModDesktop.Infrastructure.Execution;
using SimsModDesktop.Infrastructure.Mods;
using SimsModDesktop.Infrastructure.Saves;
using SimsModDesktop.Infrastructure.Services;
using SimsModDesktop.Infrastructure.TextureCompression;
using SimsModDesktop.Infrastructure.TextureProcessing;
using SimsModDesktop.Infrastructure.Tray;
using SimsModDesktop.Infrastructure.UseCases;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Infrastructure.ServiceRegistration;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IConfigurationProvider, CrossPlatformConfigurationProvider>();
        services.AddSingleton<ISimsPowerShellRunner, SimsPowerShellRunner>();

        services.AddSingleton<IFileOperationService, CrossPlatformFileOperationService>();
        services.AddSingleton<IHashComputationService, CrossPlatformHashComputationService>();

        services.AddSingleton<ImageSharpPngDecoder>();
        services.AddSingleton<PfimDdsDecoder>();
        services.AddSingleton<ITextureDecodeService, CompositeTextureDecodeService>();
        services.AddSingleton<ITextureDimensionProbe, TextureDimensionProbe>();
        services.AddSingleton<ITextureResizeService, ImageSharpResizeService>();
        services.AddSingleton<ITextureEncodeService, BcnTextureEncodeService>();
        services.AddSingleton<ITextureTranscodePipeline, TextureTranscodePipeline>();
        services.AddSingleton<ITextureCompressionService, TextureCompressionService>();

        services.AddSingleton<IModPackageTextureAnalysisStore, SqliteModPackageTextureAnalysisStore>();
        services.AddSingleton<IModPackageTextureEditStore, SqliteModPackageTextureEditStore>();
        services.AddSingleton<IModPackageTextureEditService, ModPackageTextureEditService>();
        services.AddSingleton<IModItemIndexStore, SqliteModItemIndexStore>();
        services.AddSingleton<IModItemCatalogService, SqliteModItemCatalogService>();
        services.AddSingleton<IModItemInspectService, SqliteModItemInspectService>();
        services.AddSingleton<IModPreviewCatalogService, ModPreviewCatalogService>();

        services.AddSingleton<IAppCacheMaintenanceService, AppCacheMaintenanceService>();
        services.AddSingleton<TrayThumbnailCacheStore>();
        services.AddSingleton<TrayMetadataIndexStore>();
        services.AddSingleton<TrayEmbeddedImageExtractor>();
        services.AddSingleton<ITrayMetadataService, TrayMetadataService>();
        services.AddSingleton<ITrayThumbnailService, TrayThumbnailService>();
        services.AddSingleton<ISimsTrayPreviewService, SimsTrayPreviewService>();

        services.AddSingleton<ISaveCatalogService, SaveCatalogService>();
        services.AddSingleton<ISaveHouseholdReader, SaveHouseholdReader>();
        services.AddSingleton<IHouseholdTrayExporter, HouseholdTrayExporter>();
        services.AddSingleton<ISavePreviewCacheStore, SavePreviewCacheStore>();
        services.AddSingleton<ISaveHouseholdCoordinator, SaveHouseholdCoordinator>();

        services.AddSingleton<ITS4PathDiscoveryService, TS4PathDiscoveryService>();
        services.AddSingleton<IGameLaunchService, GameLaunchService>();
        services.AddSingleton<IActionResultRepository, ActionResultRepository>();
        services.AddSingleton<IOperationRecoveryStore, SqliteOperationRecoveryStore>();

        services.AddSingleton<IOrganizeModsUseCase, NoOpOrganizeModsUseCase>();
        services.AddSingleton<IFlattenModsUseCase, NoOpFlattenModsUseCase>();
        services.AddSingleton<INormalizeNamesUseCase, NoOpNormalizeNamesUseCase>();
        services.AddSingleton<IMergeFoldersUseCase, NoOpMergeFoldersUseCase>();
        services.AddSingleton<IFindDuplicateFilesUseCase, NoOpFindDuplicateFilesUseCase>();
        services.AddSingleton<ICompressTexturesUseCase, NoOpCompressTexturesUseCase>();
        services.AddSingleton<ILoadTrayPreviewUseCase, NoOpLoadTrayPreviewUseCase>();
        services.AddSingleton<ILoadTrayPreviewPageUseCase, NoOpLoadTrayPreviewPageUseCase>();
        services.AddSingleton<IExportTraySelectionUseCase, NoOpExportTraySelectionUseCase>();
        services.AddSingleton<IAnalyzeTrayDependenciesUseCase, NoOpAnalyzeTrayDependenciesUseCase>();
        services.AddSingleton<ILoadModCatalogUseCase, NoOpLoadModCatalogUseCase>();
        services.AddSingleton<IInspectModItemUseCase, NoOpInspectModItemUseCase>();
        services.AddSingleton<ILoadSaveCatalogUseCase, NoOpLoadSaveCatalogUseCase>();
        services.AddSingleton<ILaunchGameUseCase, NoOpLaunchGameUseCase>();

        return services;
    }
}
