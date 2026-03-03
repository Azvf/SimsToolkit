using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Presentation.Shell;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.ViewModels;
using SimsModDesktop.ViewModels.Panels;
using SimsModDesktop.ViewModels.Preview;
using SimsModDesktop.ViewModels.Saves;
using SimsModDesktop.ViewModels.Shell;

namespace SimsModDesktop.Presentation.ServiceRegistration;

public static class PresentationServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ShellNavigationState>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ITrayDependenciesLauncher, TrayDependenciesLauncher>();

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
