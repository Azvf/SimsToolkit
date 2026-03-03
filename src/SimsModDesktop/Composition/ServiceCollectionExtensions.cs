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
using SimsModDesktop.Services;
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
    public static IServiceCollection AddSimsDesktopInfrastructure(this IServiceCollection services)
    {
        services.AddSimsModDesktopInfrastructure();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<IWindowHostService, WindowHostService>();
        services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
        services.AddSingleton<IConfirmationDialogService, AvaloniaConfirmationDialogService>();
        services.AddSingleton<IRecoveryPromptService, AvaloniaRecoveryPromptService>();
        services.AddSingleton<ILocalizationService, JsonLocalizationService>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IAppThemeService, AppThemeService>();

        services.AddSingleton<IExecutionEngine, PowerShellExecutionEngine>();
        services.AddSingleton<IDbpfPackageCatalog, DbpfPackageCatalog>();
        services.AddSingleton<IDbpfResourceReader, DbpfResourceReader>();
        services.AddSingleton<IPackageIndexCache, PackageIndexCache>();
        services.AddSingleton<ITrayDependencyExportService, TrayDependencyExportService>();
        services.AddSingleton<ITrayDependencyAnalysisService, TrayDependencyAnalysisService>();
        services.AddSingleton<ITrayDependenciesLauncher, TrayDependenciesLauncher>();
        return services;
    }

    public static IServiceCollection AddSimsDesktopExecution(this IServiceCollection services)
    {
        services.AddSimsModDesktopApplication();

        return services;
    }

    public static IServiceCollection AddSimsDesktopModules(this IServiceCollection services)
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
        return services;
    }

    public static IServiceCollection AddSimsDesktopPresentation(this IServiceCollection services)
    {
        services.AddSimsModDesktopPresentation();

        services.AddSingleton<ModPreviewWorkspaceViewModel>();
        services.AddSingleton<TrayPreviewWorkspaceViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SaveWorkspaceViewModel>();
        services.AddSingleton<MainShellViewModel>();
        services.AddTransient<MainWindow>();
        return services;
    }
}
