using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Cli;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Results;
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

        services.AddSingleton<IActionCliArgumentMapper, OrganizeCliArgumentMapper>();
        services.AddSingleton<IActionCliArgumentMapper, FlattenCliArgumentMapper>();
        services.AddSingleton<IActionCliArgumentMapper, NormalizeCliArgumentMapper>();
        services.AddSingleton<IActionCliArgumentMapper, MergeCliArgumentMapper>();
        services.AddSingleton<IActionCliArgumentMapper, FindDupCliArgumentMapper>();
        services.AddSingleton<ISimsCliArgumentBuilder, SimsCliArgumentBuilder>();

        services.AddSingleton<IActionInputValidator<SharedFileOpsInput>, SharedFileOpsInputValidator>();
        services.AddSingleton<IActionInputValidator<OrganizeInput>, OrganizeInputValidator>();
        services.AddSingleton<IActionInputValidator<FlattenInput>, FlattenInputValidator>();
        services.AddSingleton<IActionInputValidator<NormalizeInput>, NormalizeInputValidator>();
        services.AddSingleton<IActionInputValidator<MergeInput>, MergeInputValidator>();
        services.AddSingleton<IActionInputValidator<FindDupInput>, FindDupInputValidator>();
        services.AddSingleton<IActionInputValidator<TrayPreviewInput>, TrayPreviewInputValidator>();

        services.AddActionExecutionStrategy<OrganizeInput>(Models.SimsAction.Organize);
        services.AddActionExecutionStrategy<FlattenInput>(Models.SimsAction.Flatten);
        services.AddActionExecutionStrategy<NormalizeInput>(Models.SimsAction.Normalize);
        services.AddActionExecutionStrategy<MergeInput>(Models.SimsAction.Merge);
        services.AddActionExecutionStrategy<FindDupInput>(Models.SimsAction.FindDuplicates);

        services.AddSingleton<IExecutionEngineRoutingPolicy, ExecutionEngineRoutingPolicy>();
        services.AddSingleton<IExecutionCoordinator, ExecutionCoordinator>();
        services.AddSingleton<IFileTransformationEngine, UnifiedFileTransformationEngine>();
        services.AddSingleton<ITrayPreviewCoordinator, TrayPreviewCoordinator>();
        services.AddSingleton<IMainWindowPlanBuilder, MainWindowPlanBuilder>();
        services.AddSingleton<IToolkitExecutionRunner, ToolkitExecutionRunner>();
        services.AddSingleton<ITrayPreviewRunner, TrayPreviewRunner>();

        services.AddSingleton<IBuildBuyItemDescriptorService, BuildBuyPlaceholderDescriptorService>();
        services.AddSingleton<ICasItemDescriptorService, CasItemDescriptorService>();
        services.AddSingleton<IFastModItemIndexService, FastModItemIndexService>();
        services.AddSingleton<IDeepModItemEnrichmentService, DeepModItemEnrichmentService>();
        services.AddSingleton<IModItemIndexService, ModItemIndexService>();
        services.AddSingleton<IModItemIndexScheduler, ModItemIndexScheduler>();
        services.AddSingleton<IModPackageScanService, ModPackageScanService>();
        services.AddSingleton<IModPackageTextureAnalysisService, ModPackageTextureAnalysisService>();

        services.AddSingleton<IExecutionOutputParser, FindDupOutputParser>();
        services.AddSingleton<IExecutionOutputParser, TrayDependenciesOutputParser>();
        services.AddSingleton<IExecutionOutputParser, TrayPreviewOutputParser>();
        services.AddSingleton<IExecutionOutputParserRegistry, ExecutionOutputParserRegistry>();

        services.AddSingleton<IOperationRecoveryCoordinator, OperationRecoveryCoordinator>();
        services.AddSingleton<ISavePreviewCacheBuilder, SavePreviewCacheBuilder>();
        services.AddSingleton<IMainWindowSettingsProjection, MainWindowSettingsProjection>();

        return services;
    }

    private static void AddActionExecutionStrategy<TInput>(this IServiceCollection services, Models.SimsAction action)
        where TInput : class, Requests.ISimsExecutionInput
    {
        services.AddSingleton<IActionExecutionStrategy>(sp =>
            new ActionExecutionStrategy<TInput>(
                action,
                sp.GetRequiredService<IActionInputValidator<TInput>>(),
                sp.GetRequiredService<ISimsCliArgumentBuilder>()));
    }
}
