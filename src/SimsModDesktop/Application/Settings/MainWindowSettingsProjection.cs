using SimsModDesktop.Application.Modules;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Settings;

public sealed class MainWindowSettingsProjection : IMainWindowSettingsProjection
{
    public AppSettings Capture(MainWindowSettingsSnapshot snapshot, IActionModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        var settings = new AppSettings
        {
            UiLanguageCode = string.IsNullOrWhiteSpace(snapshot.UiLanguageCode) ? "en-US" : snapshot.UiLanguageCode.Trim(),
            ScriptPath = snapshot.ScriptPath,
            SelectedWorkspace = snapshot.Workspace,
            SelectedAction = snapshot.SelectedAction,
            WhatIf = snapshot.WhatIf,
            SharedFileOps = CloneShared(snapshot.SharedFileOps),
            UiState = CloneUiState(snapshot.UiState),
            Navigation = CloneNavigation(snapshot.Navigation),
            FeatureFlags = CloneFeatureFlags(snapshot.FeatureFlags),
            GameLaunch = CloneGameLaunch(snapshot.GameLaunch),
            Theme = CloneTheme(snapshot.Theme)
        };

        foreach (var module in moduleRegistry.All)
        {
            module.SaveToSettings(settings);
        }

        return settings;
    }

    public MainWindowResolvedSettings Resolve(AppSettings settings, IReadOnlyList<SimsAction> availableToolkitActions)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(availableToolkitActions);

        var resolvedAction = settings.SelectedAction == SimsAction.TrayPreview
            ? SimsAction.Organize
            : settings.SelectedAction;
        if (!availableToolkitActions.Contains(resolvedAction))
        {
            resolvedAction = SimsAction.Organize;
        }

        var resolvedWorkspace = Enum.IsDefined(settings.SelectedWorkspace)
            ? settings.SelectedWorkspace
            : AppWorkspace.Toolkit;

        return new MainWindowResolvedSettings
        {
            UiLanguageCode = string.IsNullOrWhiteSpace(settings.UiLanguageCode) ? "en-US" : settings.UiLanguageCode.Trim(),
            ScriptPath = settings.ScriptPath,
            WhatIf = settings.WhatIf,
            SharedFileOps = CloneShared(settings.SharedFileOps),
            UiState = CloneUiState(settings.UiState),
            Navigation = CloneNavigation(settings.Navigation),
            FeatureFlags = CloneFeatureFlags(settings.FeatureFlags),
            GameLaunch = CloneGameLaunch(settings.GameLaunch),
            Theme = CloneTheme(settings.Theme),
            SelectedAction = resolvedAction,
            Workspace = settings.SelectedAction == SimsAction.TrayPreview
                ? AppWorkspace.TrayPreview
                : resolvedWorkspace
        };
    }

    public void LoadModuleSettings(AppSettings settings, IActionModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        foreach (var module in moduleRegistry.All)
        {
            module.LoadFromSettings(settings);
        }
    }

    private static AppSettings.SharedFileOpsSettings CloneShared(AppSettings.SharedFileOpsSettings? value)
    {
        var source = value ?? new AppSettings.SharedFileOpsSettings();
        return new AppSettings.SharedFileOpsSettings
        {
            SkipPruneEmptyDirs = source.SkipPruneEmptyDirs,
            ModFilesOnly = source.ModFilesOnly,
            VerifyContentOnNameConflict = source.VerifyContentOnNameConflict,
            ModExtensionsText = source.ModExtensionsText,
            PrefixHashBytesText = source.PrefixHashBytesText,
            HashWorkerCountText = source.HashWorkerCountText
        };
    }

    private static AppSettings.UiStateSettings CloneUiState(AppSettings.UiStateSettings? value)
    {
        var source = value ?? new AppSettings.UiStateSettings();
        return new AppSettings.UiStateSettings
        {
            ToolkitLogDrawerOpen = source.ToolkitLogDrawerOpen,
            TrayPreviewLogDrawerOpen = source.TrayPreviewLogDrawerOpen,
            ToolkitAdvancedOpen = source.ToolkitAdvancedOpen,
            TrayPreviewAdvancedOpen = source.TrayPreviewAdvancedOpen
        };
    }

    private static AppSettings.NavigationSettings CloneNavigation(AppSettings.NavigationSettings? value)
    {
        var source = value ?? new AppSettings.NavigationSettings();
        return new AppSettings.NavigationSettings
        {
            SelectedSection = source.SelectedSection,
            SelectedModuleKey = source.SelectedModuleKey
        };
    }

    private static AppSettings.FeatureFlagsSettings CloneFeatureFlags(AppSettings.FeatureFlagsSettings? value)
    {
        var source = value ?? new AppSettings.FeatureFlagsSettings();
        return new AppSettings.FeatureFlagsSettings
        {
            EnableGlobalSidebarShell = source.EnableGlobalSidebarShell,
            EnableStructuredResults = source.EnableStructuredResults,
            EnableInspectorPane = source.EnableInspectorPane,
            EnableLaunchGame = source.EnableLaunchGame
        };
    }

    private static AppSettings.GameLaunchSettings CloneGameLaunch(AppSettings.GameLaunchSettings? value)
    {
        var source = value ?? new AppSettings.GameLaunchSettings();
        return new AppSettings.GameLaunchSettings
        {
            GameExecutablePath = source.GameExecutablePath,
            ModsPath = source.ModsPath,
            TrayPath = source.TrayPath,
            SavesPath = source.SavesPath
        };
    }

    private static AppSettings.ThemeSettings CloneTheme(AppSettings.ThemeSettings? value)
    {
        var source = value ?? new AppSettings.ThemeSettings();
        return new AppSettings.ThemeSettings
        {
            RequestedTheme = source.RequestedTheme
        };
    }
}
