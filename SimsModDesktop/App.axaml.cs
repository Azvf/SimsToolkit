using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Cli;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Presets;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Infrastructure.Windowing;
using SimsModDesktop.Services;
using SimsModDesktop.ViewModels;
using SimsModDesktop.Views;

namespace SimsModDesktop;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _serviceProvider = BuildServiceProvider();
            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) => _serviceProvider.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IWindowHostService, WindowHostService>();
        services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();

        services.AddSingleton<ISimsPowerShellRunner, SimsPowerShellRunner>();
        services.AddSingleton<ISimsTrayPreviewService, SimsTrayPreviewService>();
        services.AddSingleton<IQuickPresetCatalog, QuickPresetCatalog>();

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
        services.AddSingleton<IActionExecutionStrategy>(sp =>
            new ActionExecutionStrategy<OrganizeInput>(
                Models.SimsAction.Organize,
                sp.GetRequiredService<IActionInputValidator<OrganizeInput>>(),
                sp.GetRequiredService<ISimsCliArgumentBuilder>()));
        services.AddSingleton<IActionExecutionStrategy>(sp =>
            new ActionExecutionStrategy<FlattenInput>(
                Models.SimsAction.Flatten,
                sp.GetRequiredService<IActionInputValidator<FlattenInput>>(),
                sp.GetRequiredService<ISimsCliArgumentBuilder>()));
        services.AddSingleton<IActionExecutionStrategy>(sp =>
            new ActionExecutionStrategy<NormalizeInput>(
                Models.SimsAction.Normalize,
                sp.GetRequiredService<IActionInputValidator<NormalizeInput>>(),
                sp.GetRequiredService<ISimsCliArgumentBuilder>()));
        services.AddSingleton<IActionExecutionStrategy>(sp =>
            new ActionExecutionStrategy<MergeInput>(
                Models.SimsAction.Merge,
                sp.GetRequiredService<IActionInputValidator<MergeInput>>(),
                sp.GetRequiredService<ISimsCliArgumentBuilder>()));
        services.AddSingleton<IActionExecutionStrategy>(sp =>
            new ActionExecutionStrategy<FindDupInput>(
                Models.SimsAction.FindDuplicates,
                sp.GetRequiredService<IActionInputValidator<FindDupInput>>(),
                sp.GetRequiredService<ISimsCliArgumentBuilder>()));
        services.AddSingleton<IActionExecutionStrategy>(sp =>
            new ActionExecutionStrategy<TrayDependenciesInput>(
                Models.SimsAction.TrayDependencies,
                sp.GetRequiredService<IActionInputValidator<TrayDependenciesInput>>(),
                sp.GetRequiredService<ISimsCliArgumentBuilder>()));

        services.AddSingleton<IExecutionCoordinator, ExecutionCoordinator>();
        services.AddSingleton<ITrayPreviewCoordinator, TrayPreviewCoordinator>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
