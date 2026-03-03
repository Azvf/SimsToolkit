using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.ServiceRegistration;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.Infrastructure.ServiceRegistration;
using SimsModDesktop.Infrastructure.Windowing;
using SimsModDesktop.PackageCore;
using SimsModDesktop.Presentation.ServiceRegistration;
using SimsModDesktop.TrayDependencyEngine;
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
}
