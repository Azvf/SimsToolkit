using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Presentation.Shell;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;
using SimsModDesktop.Presentation.ViewModels.Saves;
using SimsModDesktop.Presentation.ViewModels.Shell;

namespace SimsModDesktop.Presentation.ServiceRegistration;

public static class PresentationServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ShellNavigationState>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ITrayDependenciesLauncher, TrayDependenciesLauncher>();
        services.AddSingleton<IUiActivityMonitor, UiActivityMonitor>();
        services.AddSingleton<IBackgroundCachePrewarmCoordinator, BackgroundCachePrewarmCoordinator>();
        services.AddSingleton<AppIdlePrewarmBootstrapper>();
        services.AddSingleton<MainWindowStatusController>();
        services.AddSingleton<MainWindowSettingsPersistenceController>();
        services.AddSingleton<MainWindowRecoveryController>();
        services.AddSingleton<MainWindowCacheWarmupController>();
        services.AddSingleton<MainWindowExecutionController>();
        services.AddSingleton<MainWindowTrayPreviewController>();
        services.AddSingleton<MainWindowTrayExportController>();
        services.AddSingleton<MainWindowValidationController>();
        services.AddSingleton<MainWindowLifecycleController>();
        services.AddSingleton<MainWindowTrayPreviewStateController>();
        services.AddSingleton<MainWindowTrayPreviewSelectionController>();
        services.AddSingleton<ShellSettingsController>();
        services.AddSingleton<ShellSystemOperationsController>();

        services.AddSingleton<OrganizePanelViewModel>();
        services.AddSingleton<TextureCompressPanelViewModel>();
        services.AddSingleton<FlattenPanelViewModel>();
        services.AddSingleton<NormalizePanelViewModel>();
        services.AddSingleton<MergePanelViewModel>();
        services.AddSingleton<FindDupPanelViewModel>();
        services.AddSingleton<TrayDependenciesPanelViewModel>();
        services.AddSingleton<ModPreviewPanelViewModel>();
        services.AddSingleton<TrayPreviewPanelViewModel>();
        services.AddSingleton<SharedFileOpsPanelViewModel>();

        services.AddSingleton<IOrganizeModuleState>(sp => sp.GetRequiredService<OrganizePanelViewModel>());
        services.AddSingleton<ITextureCompressModuleState>(sp => sp.GetRequiredService<TextureCompressPanelViewModel>());
        services.AddSingleton<IFlattenModuleState>(sp => sp.GetRequiredService<FlattenPanelViewModel>());
        services.AddSingleton<INormalizeModuleState>(sp => sp.GetRequiredService<NormalizePanelViewModel>());
        services.AddSingleton<IMergeModuleState>(sp => sp.GetRequiredService<MergePanelViewModel>());
        services.AddSingleton<IFindDupModuleState>(sp => sp.GetRequiredService<FindDupPanelViewModel>());
        services.AddSingleton<ITrayDependenciesModuleState>(sp => sp.GetRequiredService<TrayDependenciesPanelViewModel>());
        services.AddSingleton<ITrayPreviewModuleState>(sp => sp.GetRequiredService<TrayPreviewPanelViewModel>());

        services.AddSingleton<ModPreviewWorkspaceViewModel>();
        services.AddSingleton<TrayPreviewWorkspaceViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SaveWorkspaceViewModel>();
        services.AddSingleton<MainShellViewModel>();

        return services;
    }
}
