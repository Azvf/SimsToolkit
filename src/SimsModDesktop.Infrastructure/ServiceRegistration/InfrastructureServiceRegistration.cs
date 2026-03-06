using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Localization;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Services;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Application.TextureProcessing;
using SimsModDesktop.Application.Preview;
using SimsModDesktop.Infrastructure.Configuration;
using SimsModDesktop.Infrastructure.Caching;
using SimsModDesktop.Infrastructure.Localization;
using SimsModDesktop.Infrastructure.Mods;
using SimsModDesktop.Infrastructure.Saves;
using SimsModDesktop.Infrastructure.Services;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Infrastructure.TextureCompression;
using SimsModDesktop.Infrastructure.TextureProcessing;
using SimsModDesktop.Infrastructure.Tray;
using SimsModDesktop.PackageCore;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Infrastructure.ServiceRegistration;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IConfigurationProvider, CrossPlatformConfigurationProvider>();
        services.AddSingleton<ILocalizationService, JsonLocalizationService>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IDebugConfigStore, IniDebugConfigStore>();
        services.AddSingleton<IAppThemeService, AppThemeService>();

        services.AddSingleton<IFileOperationService, CrossPlatformFileOperationService>();
        services.AddSingleton<IHashComputationService, CrossPlatformHashComputationService>();
        services.AddSingleton<IPathIdentityResolver, SystemPathIdentityResolver>();

        services.AddSingleton<ImageSharpPngDecoder>();
        services.AddSingleton<PfimDdsDecoder>();
        services.AddSingleton<ITextureDecodeService, CompositeTextureDecodeService>();
        services.AddSingleton<ITextureDimensionProbe, TextureDimensionProbe>();
        services.AddSingleton<ITextureResizeService, ImageSharpResizeService>();
        services.AddSingleton<ITextureEncodeService, BcnTextureEncodeService>();
        services.AddSingleton<ITextureTranscodePipeline, TextureTranscodePipeline>();
        services.AddSingleton<ITextureCompressionService, TextureCompressionService>();
        services.AddSingleton<IListQueryCache, InMemoryListQueryCache>();

        services.AddSingleton<IModPackageTextureAnalysisStore, SqliteModPackageTextureAnalysisStore>();
        services.AddSingleton<IModPackageTextureEditStore, SqliteModPackageTextureEditStore>();
        services.AddSingleton<IModPackageTextureEditService, ModPackageTextureEditService>();
        services.AddSingleton<IModItemIndexStore>(provider =>
            new SqliteModItemIndexStore(
                provider.GetService<Microsoft.Extensions.Logging.ILogger<SqliteModItemIndexStore>>(),
                provider.GetRequiredService<IListQueryCache>()));
        services.AddSingleton<IModPackageInventoryService, SqliteModPackageInventoryService>();
        services.AddSingleton<IModItemCatalogService, SqliteModItemCatalogService>();
        services.AddSingleton<IModItemInspectService, SqliteModItemInspectService>();
        services.AddSingleton<IModPreviewCatalogService, ModPreviewCatalogService>();

        services.AddSingleton<IAppCacheMaintenanceService, AppCacheMaintenanceService>();
        services.AddSingleton<TrayThumbnailCacheStore>();
        services.AddSingleton<TrayMetadataIndexStore>();
        services.AddSingleton<ITrayPreviewRootSnapshotStore, TrayPreviewRootSnapshotStore>();
        services.AddSingleton<TrayEmbeddedImageExtractor>();
        services.AddSingleton<ITrayMetadataService, TrayMetadataService>();
        services.AddSingleton<ITrayThumbnailService, TrayThumbnailService>();
        services.AddSingleton<IPreviewQueryService, PreviewQueryService>();

        services.AddSingleton<ISaveCatalogService, SaveCatalogService>();
        services.AddSingleton<ISaveHouseholdReader, SaveHouseholdReader>();
        services.AddSingleton<ISaveAppearanceLinkService, SaveAppearanceLinkService>();
        services.AddSingleton<ILoadSaveWithAppearanceLinksService, LoadSaveWithAppearanceLinksService>();
        services.AddSingleton<ITS4SimAppearanceService>(provider =>
            (ITS4SimAppearanceService)provider.GetRequiredService<ISaveAppearanceLinkService>());
        services.AddSingleton<IHouseholdTrayExporter, HouseholdTrayExporter>();
        services.AddSingleton<ISavePreviewDescriptorStore, SavePreviewDescriptorStore>();
        services.AddSingleton<ISavePreviewDescriptorBuilder, SavePreviewDescriptorBuilder>();
        services.AddSingleton<ISavePreviewArtifactStore, SavePreviewArtifactStore>();
        services.AddSingleton<ISavePreviewArtifactProvider, SavePreviewArtifactProvider>();
        services.AddSingleton<ISaveHouseholdCoordinator, SaveHouseholdCoordinator>();

        services.AddSingleton<ITS4PathDiscoveryService, TS4PathDiscoveryService>();
        services.AddSingleton<IGameLaunchService, GameLaunchService>();
        services.AddSingleton<IActionResultRepository, ActionResultRepository>();
        services.AddSingleton<IOperationRecoveryStore, SqliteOperationRecoveryStore>();

        return services;
    }
}
