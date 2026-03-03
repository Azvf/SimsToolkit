using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Application.Validation;

namespace SimsModDesktop.Application.ServiceRegistration;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IActionInputValidator<SharedFileOpsInput>, SharedFileOpsInputValidator>();
        services.AddSingleton<IActionInputValidator<OrganizeInput>, OrganizeInputValidator>();
        services.AddSingleton<IActionInputValidator<FlattenInput>, FlattenInputValidator>();
        services.AddSingleton<IActionInputValidator<NormalizeInput>, NormalizeInputValidator>();
        services.AddSingleton<IActionInputValidator<MergeInput>, MergeInputValidator>();
        services.AddSingleton<IActionInputValidator<FindDupInput>, FindDupInputValidator>();
        services.AddSingleton<IActionInputValidator<TrayPreviewInput>, TrayPreviewInputValidator>();
        
        services.AddSingleton<IExecutionCoordinator, ExecutionCoordinator>();
        services.AddSingleton<IFileTransformationEngine, UnifiedFileTransformationEngine>();
        services.AddSingleton<ITrayPreviewCoordinator, TrayPreviewCoordinator>();
        services.AddSingleton<IToolkitActionPlanner, ToolkitActionPlanner>();
        services.AddSingleton<IBuildBuyItemDescriptorService, BuildBuyPlaceholderDescriptorService>();
        services.AddSingleton<ICasItemDescriptorService, CasItemDescriptorService>();
        services.AddSingleton<IFastModItemIndexService, FastModItemIndexService>();
        services.AddSingleton<IDeepModItemEnrichmentService, DeepModItemEnrichmentService>();
        services.AddSingleton<IModItemIndexService, ModItemIndexService>();
        services.AddSingleton<IModItemIndexScheduler, ModItemIndexScheduler>();
        services.AddSingleton<IModPackageScanService, ModPackageScanService>();
        services.AddSingleton<IModPackageTextureAnalysisService, ModPackageTextureAnalysisService>();

        services.AddSingleton<IOperationRecoveryCoordinator, OperationRecoveryCoordinator>();
        services.AddSingleton<ISavePreviewCacheBuilder, SavePreviewCacheBuilder>();
        services.AddSingleton<IMainWindowSettingsProjection, MainWindowSettingsProjection>();

        return services;
    }
}
