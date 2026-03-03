using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.ServiceRegistration;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Infrastructure.Execution;
using SimsModDesktop.Infrastructure.Localization;
using SimsModDesktop.Infrastructure.ServiceRegistration;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Infrastructure.Windowing;
using SimsModDesktop.PackageCore;
using SimsModDesktop.Presentation.ServiceRegistration;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.ViewModels.Shell;
using SimsModDesktop.ViewModels;
using SimsModDesktop.ViewModels.Panels;
using SimsModDesktop.ViewModels.Preview;
using SimsModDesktop.ViewModels.Saves;
using SimsModDesktop.Views;

namespace SimsModDesktop.Composition;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSimsDesktopShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSimsModDesktopApplication();
        services.AddSimsModDesktopPresentation();
        services.AddSimsModDesktopInfrastructure();

        services.AddDesktopShellAdapters();
        services.AddLegacyShellViewModels();

        return services;
    }

    private static IServiceCollection AddDesktopShellAdapters(this IServiceCollection services)
    {
        services.AddSingleton<IWindowHostService, WindowHostService>();
        services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
        services.AddSingleton<IConfirmationDialogService, AvaloniaConfirmationDialogService>();
        services.AddSingleton<IRecoveryPromptService, AvaloniaRecoveryPromptService>();

        services.AddSingleton<IDbpfPackageCatalog, DbpfPackageCatalog>();
        services.AddSingleton<IDbpfResourceReader, DbpfResourceReader>();
        services.AddSingleton<IPackageIndexCache, PackageIndexCache>();
        services.AddSingleton<ITrayDependencyExportService, TrayDependencyExportService>();
        services.AddSingleton<ITrayDependencyAnalysisService, TrayDependencyAnalysisService>();
        services.AddTransient<MainWindow>();

        return services;
    }

    private static IServiceCollection AddLegacyShellViewModels(this IServiceCollection services)
    {
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

        services.AddSingleton<IActionModule, OrganizeActionModule>();
        services.AddSingleton<IActionModule, FlattenActionModule>();
        services.AddSingleton<IActionModule, NormalizeActionModule>();
        services.AddSingleton<IActionModule, MergeActionModule>();
        services.AddSingleton<IActionModule, FindDupActionModule>();
        services.AddSingleton<IActionModule, TrayDependenciesActionModule>();
        services.AddSingleton<IActionModule, TrayPreviewActionModule>();
        services.AddSingleton<IActionModuleRegistry, ActionModuleRegistry>();

        services.AddSingleton<ITrayDependenciesLauncher, TrayDependenciesLauncher>();

        services.AddSingleton<ModPreviewWorkspaceViewModel>();
        services.AddSingleton<TrayPreviewWorkspaceViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SaveWorkspaceViewModel>();
        services.AddSingleton<MainShellViewModel>();

        return services;
    }
}
