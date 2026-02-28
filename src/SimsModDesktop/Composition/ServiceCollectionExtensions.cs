using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Cli;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Infrastructure.Localization;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Infrastructure.Windowing;
using SimsModDesktop.Models;
using SimsModDesktop.Services;
using SimsModDesktop.ViewModels.Inspector;
using SimsModDesktop.ViewModels.Shell;
using SimsModDesktop.ViewModels;
using SimsModDesktop.ViewModels.Panels;
using SimsModDesktop.Views;

namespace SimsModDesktop.Composition;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSimsDesktopInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IWindowHostService, WindowHostService>();
        services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
        services.AddSingleton<IConfirmationDialogService, AvaloniaConfirmationDialogService>();
        services.AddSingleton<ILocalizationService, JsonLocalizationService>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IMainWindowSettingsProjection, MainWindowSettingsProjection>();

        services.AddSingleton<ISimsPowerShellRunner, SimsPowerShellRunner>();
        services.AddSingleton<ISimsTrayPreviewService, SimsTrayPreviewService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ITS4PathDiscoveryService, TS4PathDiscoveryService>();
        services.AddSingleton<IGameLaunchService, GameLaunchService>();
        return services;
    }

    public static IServiceCollection AddSimsDesktopExecution(this IServiceCollection services)
    {
        services.AddSingleton<IActionCliArgumentMapper, OrganizeCliArgumentMapper>();
        services.AddSingleton<IActionCliArgumentMapper, FlattenCliArgumentMapper>();
        services.AddSingleton<IActionCliArgumentMapper, NormalizeCliArgumentMapper>();
        services.AddSingleton<IActionCliArgumentMapper, MergeCliArgumentMapper>();
        services.AddSingleton<IActionCliArgumentMapper, FindDupCliArgumentMapper>();
        services.AddSingleton<IActionCliArgumentMapper, TrayDependenciesCliArgumentMapper>();
        services.AddSingleton<ISimsCliArgumentBuilder, SimsCliArgumentBuilder>();

        services.AddSingleton<IActionInputValidator<SharedFileOpsInput>, SharedFileOpsInputValidator>();
        services.AddSingleton<IActionInputValidator<OrganizeInput>, OrganizeInputValidator>();
        services.AddSingleton<IActionInputValidator<FlattenInput>, FlattenInputValidator>();
        services.AddSingleton<IActionInputValidator<NormalizeInput>, NormalizeInputValidator>();
        services.AddSingleton<IActionInputValidator<MergeInput>, MergeInputValidator>();
        services.AddSingleton<IActionInputValidator<FindDupInput>, FindDupInputValidator>();
        services.AddSingleton<IActionInputValidator<TrayDependenciesInput>, TrayDependenciesInputValidator>();
        services.AddSingleton<IActionInputValidator<TrayPreviewInput>, TrayPreviewInputValidator>();

        services.AddActionExecutionStrategy<OrganizeInput>(SimsAction.Organize);
        services.AddActionExecutionStrategy<FlattenInput>(SimsAction.Flatten);
        services.AddActionExecutionStrategy<NormalizeInput>(SimsAction.Normalize);
        services.AddActionExecutionStrategy<MergeInput>(SimsAction.Merge);
        services.AddActionExecutionStrategy<FindDupInput>(SimsAction.FindDuplicates);
        services.AddActionExecutionStrategy<TrayDependenciesInput>(SimsAction.TrayDependencies);

        services.AddSingleton<IExecutionCoordinator, ExecutionCoordinator>();
        services.AddSingleton<ITrayPreviewCoordinator, TrayPreviewCoordinator>();
        services.AddSingleton<IMainWindowPlanBuilder, MainWindowPlanBuilder>();
        services.AddSingleton<IToolkitExecutionRunner, ToolkitExecutionRunner>();
        services.AddSingleton<ITrayPreviewRunner, TrayPreviewRunner>();

        services.AddSingleton<IExecutionOutputParser, TrayPreviewOutputParser>();
        services.AddSingleton<IExecutionOutputParser, TrayDependenciesOutputParser>();
        services.AddSingleton<IExecutionOutputParser, FindDupOutputParser>();
        services.AddSingleton<IExecutionOutputParserRegistry, ExecutionOutputParserRegistry>();
        services.AddSingleton<IActionResultRepository, ActionResultRepository>();
        return services;
    }

    public static IServiceCollection AddSimsDesktopModules(this IServiceCollection services)
    {
        services.AddSingleton<OrganizePanelViewModel>();
        services.AddSingleton<FlattenPanelViewModel>();
        services.AddSingleton<NormalizePanelViewModel>();
        services.AddSingleton<MergePanelViewModel>();
        services.AddSingleton<FindDupPanelViewModel>();
        services.AddSingleton<TrayDependenciesPanelViewModel>();
        services.AddSingleton<TrayPreviewPanelViewModel>();
        services.AddSingleton<SharedFileOpsPanelViewModel>();

        services.AddSingleton<IOrganizeModuleState>(sp => sp.GetRequiredService<OrganizePanelViewModel>());
        services.AddSingleton<IFlattenModuleState>(sp => sp.GetRequiredService<FlattenPanelViewModel>());
        services.AddSingleton<INormalizeModuleState>(sp => sp.GetRequiredService<NormalizePanelViewModel>());
        services.AddSingleton<IMergeModuleState>(sp => sp.GetRequiredService<MergePanelViewModel>());
        services.AddSingleton<IFindDupModuleState>(sp => sp.GetRequiredService<FindDupPanelViewModel>());
        services.AddSingleton<ITrayDependenciesModuleState>(sp => sp.GetRequiredService<TrayDependenciesPanelViewModel>());
        services.AddSingleton<ITrayPreviewModuleState>(sp => sp.GetRequiredService<TrayPreviewPanelViewModel>());

        services.AddSingleton<IActionModule>(sp => new OrganizeActionModule(sp.GetRequiredService<IOrganizeModuleState>()));
        services.AddSingleton<IActionModule>(sp => new FlattenActionModule(sp.GetRequiredService<IFlattenModuleState>()));
        services.AddSingleton<IActionModule>(sp => new NormalizeActionModule(sp.GetRequiredService<INormalizeModuleState>()));
        services.AddSingleton<IActionModule>(sp => new MergeActionModule(sp.GetRequiredService<IMergeModuleState>()));
        services.AddSingleton<IActionModule>(sp => new FindDupActionModule(sp.GetRequiredService<IFindDupModuleState>()));
        services.AddSingleton<IActionModule>(sp => new TrayDependenciesActionModule(sp.GetRequiredService<ITrayDependenciesModuleState>()));
        services.AddSingleton<IActionModule>(sp => new TrayPreviewActionModule(sp.GetRequiredService<ITrayPreviewModuleState>()));

        services.AddSingleton<IActionModuleRegistry>(sp => new ActionModuleRegistry(sp.GetServices<IActionModule>()));
        return services;
    }

    public static IServiceCollection AddSimsDesktopPresentation(this IServiceCollection services)
    {
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<IInspectorPresenter, TrayPreviewInspectorPresenter>();
        services.AddSingleton<IInspectorPresenter, TrayDependenciesInspectorPresenter>();
        services.AddSingleton<IInspectorPresenter, FindDupInspectorPresenter>();
        services.AddSingleton<InspectorViewModel>();
        services.AddSingleton<MainShellViewModel>();
        services.AddTransient<MainWindow>();
        return services;
    }

    private static void AddActionExecutionStrategy<TInput>(this IServiceCollection services, SimsAction action)
        where TInput : class, ISimsExecutionInput
    {
        services.AddSingleton<IActionExecutionStrategy>(sp =>
            new ActionExecutionStrategy<TInput>(
                action,
                sp.GetRequiredService<IActionInputValidator<TInput>>(),
                sp.GetRequiredService<ISimsCliArgumentBuilder>()));
    }
}
