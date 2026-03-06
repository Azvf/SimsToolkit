using System.Reflection;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Infrastructure.Localization;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Infrastructure.TextureCompression;
using SimsModDesktop.Infrastructure.TextureProcessing;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Tests;

public sealed class MainWindowViewModelInteractionTests
{
    [Fact]
    public async Task DangerGuard_RejectConfirmation_DoesNotExecute()
    {
        var execution = new FakeExecutionCoordinator();
        var confirmation = new FakeConfirmationDialogService { NextResult = false };
        var vm = CreateViewModel(execution, confirmation);
        await vm.InitializeAsync();

        vm.SelectedAction = SimsAction.FindDuplicates;
        vm.FindDup.Cleanup = true;
        vm.WhatIf = false;

        await InvokePrivateAsync(vm, "RunToolkitAsync");

        Assert.Equal(0, execution.ExecuteCount);
        Assert.Equal(1, confirmation.CallCount);
    }

    [Fact]
    public async Task DangerGuard_AcceptConfirmation_Executes()
    {
        var execution = new FakeExecutionCoordinator();
        var confirmation = new FakeConfirmationDialogService { NextResult = true };
        var vm = CreateViewModel(execution, confirmation);
        await vm.InitializeAsync();

        vm.SelectedAction = SimsAction.FindDuplicates;
        vm.FindDup.Cleanup = true;
        vm.WhatIf = false;

        await InvokePrivateAsync(vm, "RunToolkitAsync");

        Assert.Equal(1, execution.ExecuteCount);
        Assert.Equal(1, confirmation.CallCount);
    }

    [Fact]
    public async Task DangerGuard_WhatIfEnabled_SkipsConfirmation()
    {
        var execution = new FakeExecutionCoordinator();
        var confirmation = new FakeConfirmationDialogService { NextResult = false };
        var vm = CreateViewModel(execution, confirmation);
        await vm.InitializeAsync();

        vm.SelectedAction = SimsAction.FindDuplicates;
        vm.FindDup.Cleanup = true;
        vm.WhatIf = true;

        await InvokePrivateAsync(vm, "RunToolkitAsync");

        Assert.Equal(1, execution.ExecuteCount);
        Assert.Equal(0, confirmation.CallCount);
    }

    [Fact]
    public async Task ValidationState_DoesNotRequireScriptPath()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        vm.Workspace = AppWorkspace.Toolkit;
        InvokePrivateVoid(vm, "RefreshValidationNow");

        Assert.False(vm.HasValidationErrors);
        Assert.Contains("Validation passed", vm.ValidationSummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunToolkitAsync_TrayDependencies_UsesInternalAnalysis()
    {
        var analysisService = new FakeTrayDependencyAnalysisService();
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            trayDependencyAnalysisService: analysisService);
        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        using var modsRoot = new TempDirectory();
        vm.SelectedAction = SimsAction.TrayDependencies;
        vm.TrayDependencies.TrayPath = trayRoot.Path;
        vm.TrayDependencies.ModsPath = modsRoot.Path;
        vm.TrayDependencies.TrayItemKey = "0x123";

        InvokePrivateVoid(vm, "RefreshValidationNow");
        Assert.False(vm.HasValidationErrors);

        await InvokePrivateAsync(vm, "RunToolkitAsync");

        Assert.Equal(1, analysisService.AnalyzeCount);
        Assert.NotNull(analysisService.LastRequest);
        Assert.Equal(trayRoot.Path, analysisService.LastRequest!.TrayPath);
        Assert.Equal(modsRoot.Path, analysisService.LastRequest.ModsRootPath);
        Assert.Equal("0x123", analysisService.LastRequest.TrayItemKey);
        Assert.Contains("Tray dependencies completed", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunToolkitAsync_TextureCompress_CompressesLoosePngToDds()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "sample.png");
        var outputPath = Path.Combine(temp.Path, "sample.dds");
        await File.WriteAllBytesAsync(sourcePath, ImageTestHelpers.CreatePngBytes(8, 8));

        vm.SelectedAction = SimsAction.TextureCompress;
        vm.TextureCompress.SourcePath = sourcePath;
        vm.TextureCompress.OutputPath = outputPath;
        vm.TextureCompress.PreferredFormat = "BC3";
        vm.TextureCompress.GenerateMipMaps = false;

        await InvokePrivateAsync(vm, "RunToolkitAsync");

        Assert.True(File.Exists(outputPath));
        var bytes = await File.ReadAllBytesAsync(outputPath);
        Assert.True(bytes.Length > 4);
        Assert.Equal("DDS ", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Contains("Texture compression completed", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(outputPath, vm.TextureCompress.LastOutputPath);
    }

    [Fact]
    public async Task PersistSettings_RoundTripsUiState()
    {
        var settingsStore = new FakeSettingsStore(new AppSettings());
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            settingsStore: settingsStore);

        await vm.InitializeAsync();
        vm.IsToolkitAdvancedOpen = true;
        var preferredLanguage = vm.AvailableLanguages
            .Select(option => option.Code)
            .FirstOrDefault(code => !string.Equals(code, "en-US", StringComparison.OrdinalIgnoreCase))
            ?? "en-US";
        vm.SelectedLanguageCode = preferredLanguage;

        await vm.PersistSettingsAsync();

        Assert.NotNull(settingsStore.LastSaved);
        Assert.NotNull(settingsStore.LastSaved);
        Assert.True(settingsStore.LastSaved.UiState.ToolkitAdvancedOpen);
        Assert.Equal(vm.SelectedLanguageCode, settingsStore.LastSaved.UiLanguageCode);
    }

    [Fact]
    public async Task TrayPreviewLayoutMode_PersistsWithoutExplicitWindowClose()
    {
        var settingsStore = new FakeSettingsStore(new AppSettings
        {
            Navigation = new AppSettings.NavigationSettings
            {
                SelectedSection = AppSection.Settings
            },
            Theme = new AppSettings.ThemeSettings
            {
                RequestedTheme = "Light"
            }
        });
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            settingsStore: settingsStore);

        await vm.InitializeAsync();
        vm.TrayPreview.LayoutMode = "Grid";

        await WaitForAsync(() => settingsStore.LastSaved is not null);

        Assert.NotNull(settingsStore.LastSaved);
        Assert.Equal("Grid", settingsStore.LastSaved!.TrayPreview.LayoutMode);
        Assert.Equal(AppSection.Settings, settingsStore.LastSaved.Navigation.SelectedSection);
        Assert.Equal("Light", settingsStore.LastSaved.Theme.RequestedTheme);
    }

    [Fact]
    public async Task AvailableLanguages_ContainsDefaultLanguage()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        Assert.NotEmpty(vm.AvailableLanguages);
        Assert.Contains(vm.AvailableLanguages, option =>
            string.Equals(option.Code, "en-US", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BrowseFolder_UnknownTarget_ReportsStatusAndLog()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        await InvokePrivateAsync(vm, "BrowseFolderAsync", "UnknownFolderTarget");

        Assert.Contains("Unsupported folder browse target", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("UnknownFolderTarget", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowseCsv_UnknownTarget_ReportsStatusAndLog()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        await InvokePrivateAsync(vm, "BrowseCsvPathAsync", "UnknownCsvTarget");

        Assert.Contains("Unsupported csv browse target", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("UnknownCsvTarget", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrayPreviewEmptyState_PathMissing_ShowsMissingStatus()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        vm.Workspace = AppWorkspace.TrayPreview;
        vm.TrayPreview.TrayRoot = string.Empty;

        Assert.True(vm.IsTrayPreviewEmptyStateVisible);
        Assert.True(vm.IsTrayPreviewPathMissing);
        Assert.True(vm.IsTrayPreviewEmptyStatusMissing);
        Assert.False(vm.HasTrayPreviewItems);
        Assert.False(vm.IsTrayPreviewPagerVisible);
    }

    [Fact]
    public async Task TrayPreviewEmptyState_ValidPathButNoItems_ShowsNoResultsStatus()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        vm.Workspace = AppWorkspace.TrayPreview;
        vm.TrayPreview.TrayRoot = trayRoot.Path;

        await InvokePrivateAsync(
            vm,
            "RunTrayPreviewAsync",
            new TrayPreviewInput
            {
                PreviewSource = PreviewSourceRef.ForTrayRoot(trayRoot.Path)
            });

        Assert.True(vm.IsTrayPreviewEmptyStateVisible);
        Assert.False(vm.IsTrayPreviewPathMissing);
        Assert.True(vm.IsTrayPreviewEmptyStatusWarning);
        Assert.False(vm.HasTrayPreviewItems);
        Assert.False(vm.IsTrayPreviewPagerVisible);
    }

    [Fact]
    public async Task LoadNextTrayPreviewPageAsync_EmitsTimingLogWithCacheState()
    {
        var trayPreviewCoordinator = new FakeTrayPreviewCoordinator
        {
            NextPageFromCache = true
        };
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            trayPreviewCoordinator: trayPreviewCoordinator);
        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        await InvokePrivateAsync(
            vm,
            "RunTrayPreviewAsync",
            new TrayPreviewInput
            {
                PreviewSource = PreviewSourceRef.ForTrayRoot(trayRoot.Path)
            });

        await InvokePrivateAsync(vm, "LoadNextTrayPreviewPageAsync");

    }

    [Fact]
    public async Task TrayPreviewThumbnailFailure_StopsLoadingAndShowsErrorState()
    {
        var thumbnailService = new FailingTrayThumbnailService();
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            trayThumbnailService: thumbnailService);

        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        InvokePrivateVoid(
            vm,
            "SetTrayPreviewPage",
            new SimsTrayPreviewPage
            {
                PageIndex = 1,
                PageSize = 50,
                TotalItems = 1,
                TotalPages = 1,
                Items =
                [
                    new SimsTrayPreviewItem
                    {
                        TrayItemKey = "0x0000000000000042",
                        PresetType = "Household",
                        TrayRootPath = trayRoot.Path
                    }
                ]
            },
            1);

        await WaitForAsync(() => vm.PreviewItems.Count == 1 && !vm.PreviewItems[0].IsThumbnailLoading);

        Assert.Equal(1, thumbnailService.CallCount);
        Assert.False(vm.PreviewItems[0].HasThumbnail);
        Assert.True(vm.PreviewItems[0].HasThumbnailError);
        Assert.False(vm.PreviewItems[0].IsThumbnailLoading);
    }

    [Fact]
    public async Task TrayPreviewSelection_SupportsSingleCtrlAndShiftRange()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        InvokePrivateVoid(
            vm,
            "SetTrayPreviewPage",
            new SimsTrayPreviewPage
            {
                PageIndex = 1,
                PageSize = 50,
                TotalItems = 4,
                TotalPages = 1,
                Items =
                [
                    new SimsTrayPreviewItem { TrayItemKey = "0x1", PresetType = "Household", TrayRootPath = trayRoot.Path },
                    new SimsTrayPreviewItem { TrayItemKey = "0x2", PresetType = "Household", TrayRootPath = trayRoot.Path },
                    new SimsTrayPreviewItem { TrayItemKey = "0x3", PresetType = "Household", TrayRootPath = trayRoot.Path },
                    new SimsTrayPreviewItem { TrayItemKey = "0x4", PresetType = "Household", TrayRootPath = trayRoot.Path }
                ]
            },
            1);

        await WaitForAsync(() => vm.PreviewItems.Count == 4);

        vm.ApplyTrayPreviewSelection(vm.PreviewItems[0], controlPressed: false, shiftPressed: false);
        Assert.Equal(1, vm.SelectedTrayPreviewCount);
        Assert.True(vm.PreviewItems[0].IsSelected);
        Assert.False(vm.PreviewItems[1].IsSelected);

        vm.ApplyTrayPreviewSelection(vm.PreviewItems[2], controlPressed: false, shiftPressed: true);
        Assert.Equal(3, vm.SelectedTrayPreviewCount);
        Assert.True(vm.PreviewItems[0].IsSelected);
        Assert.True(vm.PreviewItems[1].IsSelected);
        Assert.True(vm.PreviewItems[2].IsSelected);
        Assert.False(vm.PreviewItems[3].IsSelected);

        vm.ApplyTrayPreviewSelection(vm.PreviewItems[1], controlPressed: true, shiftPressed: false);
        Assert.Equal(2, vm.SelectedTrayPreviewCount);
        Assert.True(vm.PreviewItems[0].IsSelected);
        Assert.False(vm.PreviewItems[1].IsSelected);
        Assert.True(vm.PreviewItems[2].IsSelected);
        Assert.False(vm.PreviewItems[3].IsSelected);

        vm.ApplyTrayPreviewSelection(vm.PreviewItems[0], controlPressed: false, shiftPressed: false);
        Assert.Equal(1, vm.SelectedTrayPreviewCount);
        Assert.Equal("1 selected / 4 on page / 4 total", vm.TrayPreviewSelectionSummaryText);
        Assert.False(vm.PreviewItems[0].IsSelected);
        Assert.False(vm.PreviewItems[1].IsSelected);
        Assert.True(vm.PreviewItems[2].IsSelected);
        Assert.False(vm.PreviewItems[3].IsSelected);

        vm.SelectAllTrayPreviewPageCommand.Execute(null);
        Assert.Equal(4, vm.SelectedTrayPreviewCount);
        Assert.Equal("4 selected / 4 on page / 4 total", vm.TrayPreviewSelectionSummaryText);
        Assert.All(vm.PreviewItems, item => Assert.True(item.IsSelected));

        vm.ClearTrayPreviewSelectionCommand.Execute(null);
        Assert.Equal(0, vm.SelectedTrayPreviewCount);
        Assert.Equal("0 selected / 4 on page / 4 total", vm.TrayPreviewSelectionSummaryText);
        Assert.All(vm.PreviewItems, item => Assert.False(item.IsSelected));
    }

    [Fact]
    public async Task ExportSelectedTrayPreviewFiles_ExportsOnlySelectedSourceFiles()
    {
        var fileDialog = new FakeFileDialogService();
        var exportService = new FakeTrayDependencyExportService
        {
            OnExport = request =>
            {
                Directory.CreateDirectory(request.TrayExportRoot);
                Directory.CreateDirectory(request.ModsExportRoot);
                foreach (var sourceFile in request.TraySourceFiles)
                {
                    File.Copy(sourceFile, System.IO.Path.Combine(request.TrayExportRoot, System.IO.Path.GetFileName(sourceFile)));
                }

                File.WriteAllText(System.IO.Path.Combine(request.ModsExportRoot, "resolved.package"), "mod");
                return new TrayDependencyExportResult
                {
                    Success = true,
                    CopiedTrayFileCount = request.TraySourceFiles.Count,
                    CopiedModFileCount = 1
                };
            }
        };
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            trayDependencyExportService: exportService,
            fileDialogService: fileDialog);
        await vm.InitializeAsync();
        vm.Workspace = AppWorkspace.TrayPreview;

        using var trayRoot = new TempDirectory();
        using var modsRoot = new TempDirectory();
        using var exportRoot = new TempDirectory();
        var sourceA = System.IO.Path.Combine(trayRoot.Path, "0x1.trayitem");
        var sourceB = System.IO.Path.Combine(trayRoot.Path, "0x2.trayitem");
        File.WriteAllText(sourceA, "a");
        File.WriteAllText(sourceB, "b");
        vm.TrayDependencies.ModsPath = modsRoot.Path;

        InvokePrivateVoid(
            vm,
            "SetTrayPreviewPage",
            new SimsTrayPreviewPage
            {
                PageIndex = 1,
                PageSize = 50,
                TotalItems = 2,
                TotalPages = 1,
                Items =
                [
                    new SimsTrayPreviewItem
                    {
                        TrayItemKey = "0x1",
                        PresetType = "Household",
                        TrayRootPath = trayRoot.Path,
                        SourceFilePaths = [sourceA]
                    },
                    new SimsTrayPreviewItem
                    {
                        TrayItemKey = "0x2",
                        PresetType = "Household",
                        TrayRootPath = trayRoot.Path,
                        SourceFilePaths = [sourceB]
                    }
                ]
            },
            1);

        await WaitForAsync(() => vm.PreviewItems.Count == 2);
        vm.ApplyTrayPreviewSelection(vm.PreviewItems[1], controlPressed: false, shiftPressed: false);
        await vm.TrayPreviewWorkspace.EnsureLoadedAsync(forceReload: false);
        fileDialog.NextFolderPaths = [exportRoot.Path];

        await InvokePrivateAsync(vm, "ExportSelectedTrayPreviewFilesAsync");
        Dispatcher.UIThread.RunJobs(null);

        Assert.Empty(Directory.GetFiles(exportRoot.Path, "0x1.trayitem", SearchOption.AllDirectories));
        var exportedFiles = Directory.GetFiles(exportRoot.Path, "0x2.trayitem", SearchOption.AllDirectories);
        Assert.Single(exportedFiles);
        Assert.Contains($"{System.IO.Path.DirectorySeparatorChar}Tray{System.IO.Path.DirectorySeparatorChar}", exportedFiles[0]);
        Assert.Equal("Exported 1 tray files and 1 referenced mod files.", vm.StatusMessage);
        Assert.Single(vm.TrayExportTasks);
        Assert.True(vm.TrayExportTasks[0].IsCompleted);
        Assert.False(vm.TrayExportTasks[0].IsFailed);
        Assert.Equal("Completed.", vm.TrayExportTasks[0].StatusText);
        Assert.True(vm.TrayExportTasks[0].HasExportRoot);
        Assert.Equal("100%", vm.TrayExportTasks[0].ProgressText);
        Assert.Equal(100d, vm.TrayExportTasks[0].ProgressPercent);

        vm.ClearCompletedTrayExportTasksCommand.Execute(null);
        Assert.Empty(vm.TrayExportTasks);
    }

    [Fact]
    public async Task ExportSelectedTrayPreviewFiles_SetupFailureWhenModsPathMissing_DoesNotDuplicateDetails()
    {
        var fileDialog = new FakeFileDialogService();
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            fileDialogService: fileDialog);
        await vm.InitializeAsync();
        vm.Workspace = AppWorkspace.TrayPreview;

        using var trayRoot = new TempDirectory();
        using var exportRoot = new TempDirectory();
        var source = System.IO.Path.Combine(trayRoot.Path, "0x2.trayitem");
        File.WriteAllText(source, "b");

        InvokePrivateVoid(
            vm,
            "SetTrayPreviewPage",
            new SimsTrayPreviewPage
            {
                PageIndex = 1,
                PageSize = 50,
                TotalItems = 1,
                TotalPages = 1,
                Items =
                [
                    new SimsTrayPreviewItem
                    {
                        TrayItemKey = "0x2",
                        PresetType = "Household",
                        TrayRootPath = trayRoot.Path,
                        SourceFilePaths = [source]
                    }
                ]
            },
            1);

        await WaitForAsync(() => vm.PreviewItems.Count == 1);
        vm.ApplyTrayPreviewSelection(vm.PreviewItems[0], controlPressed: false, shiftPressed: false);
        await vm.TrayPreviewWorkspace.EnsureLoadedAsync(forceReload: false);
        fileDialog.NextFolderPaths = [exportRoot.Path];

        await InvokePrivateAsync(vm, "ExportSelectedTrayPreviewFilesAsync");
        Dispatcher.UIThread.RunJobs(null);

        Assert.Equal("Mods Path is missing. Set a valid Mods Path before exporting referenced mods.", vm.StatusMessage);
        Assert.Single(vm.TrayExportTasks);
        Assert.True(vm.IsTrayExportQueueDockVisible);
        Assert.True(vm.IsTrayExportQueueVisible);
        Assert.Equal("Hide Tasks", vm.TrayExportQueueToggleText);
        Assert.Equal("Failed", vm.TrayExportTasks[0].ProgressText);
        Assert.False(vm.TrayExportTasks[0].HasDetails);
        Assert.False(vm.TrayExportTasks[0].CanShowDetailsToggle);

        vm.ToggleTrayExportQueueCommand.Execute(null);
        Assert.True(vm.IsTrayExportQueueDockVisible);
        Assert.False(vm.IsTrayExportQueueVisible);
        Assert.Equal("Show Tasks (1)", vm.TrayExportQueueToggleText);

        vm.ToggleTrayExportQueueCommand.Execute(null);
        Assert.True(vm.IsTrayExportQueueVisible);
        Assert.Equal("Hide Tasks", vm.TrayExportQueueToggleText);
    }

    [Fact]
    public async Task ExportSelectedTrayPreviewFiles_FailsAndRollsBackWhenModExportFails()
    {
        var fileDialog = new FakeFileDialogService();
        var exportService = new FakeTrayDependencyExportService
        {
            OnExport = request =>
            {
                Directory.CreateDirectory(request.TrayExportRoot);
                Directory.CreateDirectory(request.ModsExportRoot);
                foreach (var sourceFile in request.TraySourceFiles)
                {
                    File.Copy(sourceFile, System.IO.Path.Combine(request.TrayExportRoot, System.IO.Path.GetFileName(sourceFile)));
                }

                File.WriteAllText(System.IO.Path.Combine(request.ModsExportRoot, "broken.package"), "partial");
                return new TrayDependencyExportResult
                {
                    Success = false,
                    CopiedTrayFileCount = request.TraySourceFiles.Count,
                    Issues =
                    [
                        new TrayDependencyIssue
                        {
                            Severity = TrayDependencyIssueSeverity.Error,
                            Kind = TrayDependencyIssueKind.CopyError,
                            Message = "mod export crashed"
                        },
                        new TrayDependencyIssue
                        {
                            Severity = TrayDependencyIssueSeverity.Warning,
                            Kind = TrayDependencyIssueKind.MissingReference,
                            Message = "Access denied while exporting foo.package"
                        }
                    ]
                };
            }
        };
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            trayDependencyExportService: exportService,
            fileDialogService: fileDialog);
        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        using var modsRoot = new TempDirectory();
        using var exportRoot = new TempDirectory();
        var source = System.IO.Path.Combine(trayRoot.Path, "0x2.trayitem");
        File.WriteAllText(source, "b");
        vm.TrayDependencies.ModsPath = modsRoot.Path;

        InvokePrivateVoid(
            vm,
            "SetTrayPreviewPage",
            new SimsTrayPreviewPage
            {
                PageIndex = 1,
                PageSize = 50,
                TotalItems = 1,
                TotalPages = 1,
                Items =
                [
                    new SimsTrayPreviewItem
                    {
                        TrayItemKey = "0x2",
                        PresetType = "Household",
                        TrayRootPath = trayRoot.Path,
                        SourceFilePaths = [source]
                    }
                ]
            },
            1);

        await WaitForAsync(() => vm.PreviewItems.Count == 1);
        vm.ApplyTrayPreviewSelection(vm.PreviewItems[0], controlPressed: false, shiftPressed: false);
        await vm.TrayPreviewWorkspace.EnsureLoadedAsync(forceReload: false);
        fileDialog.NextFolderPaths = [exportRoot.Path];

        await InvokePrivateAsync(vm, "ExportSelectedTrayPreviewFilesAsync");
        Dispatcher.UIThread.RunJobs(null);

        Assert.Equal("Export failed: mod export crashed", vm.StatusMessage);
        Assert.Empty(Directory.EnumerateFileSystemEntries(exportRoot.Path));
        Assert.Single(vm.TrayExportTasks);
        Assert.True(vm.TrayExportTasks[0].IsCompleted);
        Assert.True(vm.TrayExportTasks[0].IsFailed);
        Assert.Equal("99%", vm.TrayExportTasks[0].ProgressText);
        Assert.Equal("Mods export failed: mod export crashed", vm.TrayExportTasks[0].StatusText);
        Assert.True(vm.TrayExportTasks[0].CanShowDetailsToggle);
        Assert.Contains("Access denied while exporting foo.package", vm.TrayExportTasks[0].DetailsText, StringComparison.Ordinal);
        Assert.Contains("mod export crashed", vm.TrayExportTasks[0].DetailsText, StringComparison.Ordinal);

        vm.ToggleTrayExportTaskDetailsCommand.Execute(vm.TrayExportTasks[0]);
        Assert.True(vm.TrayExportTasks[0].IsDetailsExpanded);
    }

    [Fact]
    public async Task ExportSelectedTrayPreviewFiles_MultiSelect_FailFastRollsBackWholeBatch()
    {
        var fileDialog = new FakeFileDialogService();
        var startedCount = 0;
        var exportService = new FakeTrayDependencyExportService
        {
            OnExportAsync = async (request, cancellationToken) =>
            {
                Interlocked.Increment(ref startedCount);
                Directory.CreateDirectory(request.TrayExportRoot);
                Directory.CreateDirectory(request.ModsExportRoot);
                foreach (var sourceFile in request.TraySourceFiles)
                {
                    File.Copy(sourceFile, System.IO.Path.Combine(request.TrayExportRoot, System.IO.Path.GetFileName(sourceFile)));
                }

                if (string.Equals(request.TrayItemKey, "0x8", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(60, cancellationToken);
                    return new TrayDependencyExportResult
                    {
                        Success = false,
                        CopiedTrayFileCount = request.TraySourceFiles.Count,
                        Issues =
                        [
                            new TrayDependencyIssue
                            {
                                Severity = TrayDependencyIssueSeverity.Error,
                                Kind = TrayDependencyIssueKind.CopyError,
                                Message = "mod export crashed"
                            }
                        ]
                    };
                }

                if (string.Equals(request.TrayItemKey, "0x1", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(10, cancellationToken);
                }
                else
                {
                    await Task.Delay(120, cancellationToken);
                }

                File.WriteAllText(System.IO.Path.Combine(request.ModsExportRoot, request.TrayItemKey + ".package"), "ok");
                return new TrayDependencyExportResult
                {
                    Success = true,
                    CopiedTrayFileCount = request.TraySourceFiles.Count,
                    CopiedModFileCount = 1
                };
            }
        };
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            trayDependencyExportService: exportService,
            fileDialogService: fileDialog);
        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        using var modsRoot = new TempDirectory();
        using var exportRoot = new TempDirectory();
        vm.TrayDependencies.ModsPath = modsRoot.Path;

        var previewItems = new List<SimsTrayPreviewItem>();
        for (var i = 1; i <= 12; i++)
        {
            var trayKey = $"0x{i:x}";
            var sourcePath = System.IO.Path.Combine(trayRoot.Path, trayKey + ".trayitem");
            File.WriteAllText(sourcePath, $"item-{i}");
            previewItems.Add(new SimsTrayPreviewItem
            {
                TrayItemKey = trayKey,
                PresetType = "Household",
                TrayRootPath = trayRoot.Path,
                SourceFilePaths = [sourcePath]
            });
        }

        InvokePrivateVoid(
            vm,
            "SetTrayPreviewPage",
            new SimsTrayPreviewPage
            {
                PageIndex = 1,
                PageSize = 50,
                TotalItems = previewItems.Count,
                TotalPages = 1,
                Items = previewItems
            },
            1);

        await WaitForAsync(() => vm.PreviewItems.Count == 12);
        vm.SelectAllTrayPreviewPageCommand.Execute(null);
        await vm.TrayPreviewWorkspace.EnsureLoadedAsync(forceReload: false);
        fileDialog.NextFolderPaths = [exportRoot.Path];

        await InvokePrivateAsync(vm, "ExportSelectedTrayPreviewFilesAsync");
        Dispatcher.UIThread.RunJobs(null);

        Assert.Equal("Export failed: mod export crashed", vm.StatusMessage);
        Assert.Equal(12, vm.TrayExportTasks.Count);
        Assert.True(startedCount < 12);
        Assert.Empty(Directory.EnumerateFileSystemEntries(exportRoot.Path));
        Assert.Contains(vm.TrayExportTasks, task => string.Equals(task.StatusText, "Rolled back after batch failure.", StringComparison.Ordinal));
        Assert.Contains(vm.TrayExportTasks, task => string.Equals(task.StatusText, "Cancelled after batch failure.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportSelectedTrayPreviewFiles_MultiSelect_UsesBoundedConcurrency()
    {
        var fileDialog = new FakeFileDialogService();
        var currentConcurrency = 0;
        var maxConcurrency = 0;

        static void UpdateMax(ref int observed, int current)
        {
            while (true)
            {
                var snapshot = Volatile.Read(ref observed);
                if (current <= snapshot)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref observed, current, snapshot) == snapshot)
                {
                    return;
                }
            }
        }

        var exportService = new FakeTrayDependencyExportService
        {
            OnExportAsync = async (request, cancellationToken) =>
            {
                var concurrency = Interlocked.Increment(ref currentConcurrency);
                UpdateMax(ref maxConcurrency, concurrency);
                try
                {
                    Directory.CreateDirectory(request.TrayExportRoot);
                    Directory.CreateDirectory(request.ModsExportRoot);
                    foreach (var sourceFile in request.TraySourceFiles)
                    {
                        File.Copy(sourceFile, System.IO.Path.Combine(request.TrayExportRoot, System.IO.Path.GetFileName(sourceFile)));
                    }

                    await Task.Delay(100, cancellationToken);
                    File.WriteAllText(System.IO.Path.Combine(request.ModsExportRoot, request.TrayItemKey + ".package"), "ok");
                    return new TrayDependencyExportResult
                    {
                        Success = true,
                        CopiedTrayFileCount = request.TraySourceFiles.Count,
                        CopiedModFileCount = 1
                    };
                }
                finally
                {
                    Interlocked.Decrement(ref currentConcurrency);
                }
            }
        };
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            trayDependencyExportService: exportService,
            fileDialogService: fileDialog);
        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        using var modsRoot = new TempDirectory();
        using var exportRoot = new TempDirectory();
        vm.TrayDependencies.ModsPath = modsRoot.Path;

        var previewItems = new List<SimsTrayPreviewItem>();
        for (var i = 1; i <= 12; i++)
        {
            var trayKey = $"0x{i:x}";
            var sourcePath = System.IO.Path.Combine(trayRoot.Path, trayKey + ".trayitem");
            File.WriteAllText(sourcePath, $"item-{i}");
            previewItems.Add(new SimsTrayPreviewItem
            {
                TrayItemKey = trayKey,
                PresetType = "Household",
                TrayRootPath = trayRoot.Path,
                SourceFilePaths = [sourcePath]
            });
        }

        InvokePrivateVoid(
            vm,
            "SetTrayPreviewPage",
            new SimsTrayPreviewPage
            {
                PageIndex = 1,
                PageSize = 50,
                TotalItems = previewItems.Count,
                TotalPages = 1,
                Items = previewItems
            },
            1);

        await WaitForAsync(() => vm.PreviewItems.Count == 12);
        vm.SelectAllTrayPreviewPageCommand.Execute(null);
        await vm.TrayPreviewWorkspace.EnsureLoadedAsync(forceReload: false);
        fileDialog.NextFolderPaths = [exportRoot.Path];

        await InvokePrivateAsync(vm, "ExportSelectedTrayPreviewFilesAsync");
        Dispatcher.UIThread.RunJobs(null);

        Assert.True(maxConcurrency <= 8);
        Assert.Equal("Exported 12 tray files and 12 referenced mod files.", vm.StatusMessage);
        Assert.Equal(12, vm.TrayExportTasks.Count);
        Assert.All(vm.TrayExportTasks, task => Assert.True(task.IsCompleted && !task.IsFailed));
    }

    [Fact]
    public async Task ExportSelectedTrayPreviewFiles_IgnoresMissingPackageFileFailuresForMods()
    {
        var fileDialog = new FakeFileDialogService();
        var exportService = new FakeTrayDependencyExportService
        {
            OnExport = request =>
            {
                Directory.CreateDirectory(request.TrayExportRoot);
                Directory.CreateDirectory(request.ModsExportRoot);
                foreach (var sourceFile in request.TraySourceFiles)
                {
                    File.Copy(sourceFile, System.IO.Path.Combine(request.TrayExportRoot, System.IO.Path.GetFileName(sourceFile)));
                }

                return new TrayDependencyExportResult
                {
                    Success = true,
                    CopiedTrayFileCount = request.TraySourceFiles.Count,
                    CopiedModFileCount = 0,
                    HasMissingReferenceWarnings = true,
                    Issues =
                    [
                        new TrayDependencyIssue
                        {
                            Severity = TrayDependencyIssueSeverity.Warning,
                            Kind = TrayDependencyIssueKind.MissingReference,
                            Message = "Missing package file: missing-test.package"
                        }
                    ]
                };
            }
        };
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            trayDependencyExportService: exportService,
            fileDialogService: fileDialog);
        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        using var modsRoot = new TempDirectory();
        using var exportRoot = new TempDirectory();
        var source = System.IO.Path.Combine(trayRoot.Path, "0x2.trayitem");
        File.WriteAllText(source, "b");
        vm.TrayDependencies.ModsPath = modsRoot.Path;

        InvokePrivateVoid(
            vm,
            "SetTrayPreviewPage",
            new SimsTrayPreviewPage
            {
                PageIndex = 1,
                PageSize = 50,
                TotalItems = 1,
                TotalPages = 1,
                Items =
                [
                    new SimsTrayPreviewItem
                    {
                        TrayItemKey = "0x2",
                        PresetType = "Household",
                        TrayRootPath = trayRoot.Path,
                        SourceFilePaths = [source]
                    }
                ]
            },
            1);

        await WaitForAsync(() => vm.PreviewItems.Count == 1);
        vm.ApplyTrayPreviewSelection(vm.PreviewItems[0], controlPressed: false, shiftPressed: false);
        await vm.TrayPreviewWorkspace.EnsureLoadedAsync(forceReload: false);
        fileDialog.NextFolderPaths = [exportRoot.Path];

        await InvokePrivateAsync(vm, "ExportSelectedTrayPreviewFilesAsync");
        Dispatcher.UIThread.RunJobs(null);

        var exportedFiles = Directory.GetFiles(exportRoot.Path, "0x2.trayitem", SearchOption.AllDirectories);
        Assert.Single(exportedFiles);
        Assert.Equal("Exported 1 tray files and 0 referenced mod files (1 warning item(s) ignored).", vm.StatusMessage);
        Assert.Single(vm.TrayExportTasks);
        Assert.True(vm.TrayExportTasks[0].IsCompleted);
        Assert.False(vm.TrayExportTasks[0].IsFailed);
        Assert.Equal("Completed (missing references ignored).", vm.TrayExportTasks[0].StatusText);
        Assert.Contains("Missing package file: missing-test.package", vm.TrayExportTasks[0].DetailsText, StringComparison.Ordinal);
    }

    private static MainWindowViewModel CreateViewModel(
        FakeExecutionCoordinator execution,
        FakeConfirmationDialogService confirmation,
        FakeSettingsStore? settingsStore = null,
        ITrayDependencyExportService? trayDependencyExportService = null,
        ITrayDependencyAnalysisService? trayDependencyAnalysisService = null,
        ITrayThumbnailService? trayThumbnailService = null,
        FakeFileDialogService? fileDialogService = null,
        FakeTrayPreviewCoordinator? trayPreviewCoordinator = null,
        ILoggerFactory? loggerFactory = null)
    {
        var organize = new OrganizePanelViewModel();
        var textureCompress = new TextureCompressPanelViewModel();
        var flatten = new FlattenPanelViewModel();
        var normalize = new NormalizePanelViewModel();
        var merge = new MergePanelViewModel();
        var findDup = new FindDupPanelViewModel();
        var trayDependencies = new TrayDependenciesPanelViewModel();
        var modPreview = new ModPreviewPanelViewModel();
        var trayPreview = new TrayPreviewPanelViewModel();
        var sharedFileOps = new SharedFileOpsPanelViewModel();
        var cacheWarmupController = new MainWindowCacheWarmupController(
            new NoOpModPackageInventoryService(),
            new NoOpModItemIndexScheduler(),
            new FakePackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance);
        trayPreviewCoordinator ??= new FakeTrayPreviewCoordinator();
        var trayPreviewWorkspace = new TrayPreviewWorkspaceViewModel(
            trayPreview,
            trayPreviewCoordinator,
            trayThumbnailService ?? new FailingTrayThumbnailService(),
            fileDialogService ?? new FakeFileDialogService(),
            trayDependencyExportService ?? new FakeTrayDependencyExportService(),
            cacheWarmupController,
            trayDependencies);
        var modPreviewWorkspace = new ModPreviewWorkspaceViewModel(
            modPreview,
            new NoOpModItemCatalogService(),
            new NoOpModItemIndexScheduler(),
            cacheWarmupController,
            new NoOpModItemInspectService(),
            NullModPackageTextureEditService.Instance,
            fileDialogService ?? new FakeFileDialogService());
        var toolkitActionPlanner = new ToolkitActionPlanner(
            organize,
            textureCompress,
            flatten,
            normalize,
            merge,
            findDup,
            trayDependencies,
            trayPreview);
        var recoveryController = new MainWindowRecoveryController();
        var trayPreviewStateController = new MainWindowTrayPreviewStateController();
        var trayPreviewSelectionController = new MainWindowTrayPreviewSelectionController();
        var effectiveTrayThumbnailService = trayThumbnailService ?? new FailingTrayThumbnailService();
        var effectiveTrayExportService = trayDependencyExportService ?? new FakeTrayDependencyExportService();
        var settingsPersistenceController = new MainWindowSettingsPersistenceController(settingsStore ?? new FakeSettingsStore(new AppSettings()));
        var settingsProjection = new MainWindowSettingsProjection();

        return new MainWindowViewModel(
            fileDialogService ?? new FakeFileDialogService(),
            confirmation,
            new JsonLocalizationService(),
            toolkitActionPlanner,
            new MainWindowExecutionController(
                execution,
                trayDependencyAnalysisService ?? new FakeTrayDependencyAnalysisService(),
                toolkitActionPlanner,
                recoveryController,
                cacheWarmupController,
                CreateTextureCompressionService(),
                new TextureDimensionProbe(),
                loggerFactory?.CreateLogger<MainWindowExecutionController>() ?? NullLogger<MainWindowExecutionController>.Instance),
            new MainWindowStatusController(),
            recoveryController,
            new MainWindowTrayPreviewController(
                trayPreviewCoordinator,
                effectiveTrayThumbnailService,
                toolkitActionPlanner,
                recoveryController,
                trayPreviewStateController,
                trayPreviewSelectionController,
                loggerFactory?.CreateLogger<MainWindowTrayPreviewController>() ?? NullLogger<MainWindowTrayPreviewController>.Instance),
            new MainWindowTrayExportController(
                effectiveTrayExportService,
                cacheWarmupController,
                loggerFactory?.CreateLogger<MainWindowTrayExportController>() ?? NullLogger<MainWindowTrayExportController>.Instance),
            new MainWindowValidationController(toolkitActionPlanner),
            new MainWindowLifecycleController(
                settingsPersistenceController,
                settingsProjection,
                recoveryController,
                toolkitActionPlanner,
                loggerFactory?.CreateLogger<MainWindowLifecycleController>() ?? NullLogger<MainWindowLifecycleController>.Instance),
            trayPreviewStateController,
            trayPreviewSelectionController,
            modPreviewWorkspace,
            trayPreviewWorkspace,
            organize,
            textureCompress,
            flatten,
            normalize,
            merge,
            findDup,
            trayDependencies: trayDependencies,
            modPreview: modPreview,
            trayPreview: trayPreview,
            sharedFileOps: sharedFileOps,
            logger: loggerFactory?.CreateLogger<MainWindowViewModel>());
    }

    private static ITextureCompressionService CreateTextureCompressionService()
    {
        var decodeService = new CompositeTextureDecodeService(new ImageSharpPngDecoder(), new PfimDdsDecoder());
        var pipeline = new TextureTranscodePipeline(
            decodeService,
            new ImageSharpResizeService(),
            new BcnTextureEncodeService());
        return new TextureCompressionService(decodeService, pipeline);
    }

    private static async Task InvokePrivateAsync(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = method!.Invoke(target, args) as Task;
        Assert.NotNull(task);
        await task!;
    }

    private static void InvokePrivateVoid(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, args);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 1000)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs(null);
            if ((DateTime.UtcNow - startedAt).TotalMilliseconds > timeoutMs)
            {
                break;
            }

            await Task.Delay(10);
        }

        Dispatcher.UIThread.RunJobs(null);
        Assert.True(condition());
    }

    private sealed class FakePackageIndexCache : IPackageIndexCache
    {
        private static readonly IndexedPackageFile[] FakePackages =
        [
            new IndexedPackageFile
            {
                FilePath = "fake.package",
                Length = 1,
                LastWriteTimeUtc = DateTime.UtcNow,
                Entries = Array.Empty<PackageIndexEntry>(),
                TypeIndexes = new Dictionary<uint, PackageTypeIndex>()
            }
        ];

        public Task<PackageIndexSnapshot?> TryLoadSnapshotAsync(
            string modsRootPath,
            long inventoryVersion,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PackageIndexSnapshot?>(new PackageIndexSnapshot
            {
                ModsRootPath = string.IsNullOrWhiteSpace(modsRootPath)
                    ? string.Empty
                    : Path.GetFullPath(modsRootPath.Trim()),
                InventoryVersion = inventoryVersion <= 0 ? 1 : inventoryVersion,
                Packages = FakePackages
            });
        }

        public Task<PackageIndexSnapshot> BuildSnapshotAsync(
            PackageIndexBuildRequest request,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PackageIndexSnapshot
            {
                ModsRootPath = request.ModsRootPath,
                InventoryVersion = request.InventoryVersion,
                Packages = FakePackages
            });
        }

    }

    private sealed class FakeExecutionCoordinator : IExecutionCoordinator
    {
        public int ExecuteCount { get; private set; }
        public int NextExitCode { get; set; }
        public IReadOnlyList<string> NextOutputLines { get; set; } = Array.Empty<string>();
        public IReadOnlyList<SimsProgressUpdate> NextProgressUpdates { get; set; } = Array.Empty<SimsProgressUpdate>();
        public string? NextExceptionMessage { get; set; }

        public Task<SimsExecutionResult> ExecuteAsync(ISimsExecutionInput input, Action<string> onOutput, Action<SimsProgressUpdate>? onProgress = null, CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            foreach (var progress in NextProgressUpdates)
            {
                onProgress?.Invoke(progress);
            }

            foreach (var line in NextOutputLines)
            {
                onOutput(line);
            }

            if (!string.IsNullOrWhiteSpace(NextExceptionMessage))
            {
                throw new InvalidOperationException(NextExceptionMessage);
            }

            return Task.FromResult(new SimsExecutionResult
            {
                ExitCode = NextExitCode,
                Executable = "pwsh",
                Arguments = Array.Empty<string>()
            });
        }
    }

    private sealed class NoOpModItemCatalogService : IModItemCatalogService
    {
        public Task<ModItemCatalogPage> QueryPageAsync(ModItemCatalogQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new ModItemCatalogPage
            {
                Items = Array.Empty<ModItemListRow>(),
                TotalItems = 0,
                PageIndex = 1,
                PageSize = 50,
                TotalPages = 0
            });
    }

    private sealed class NoOpModItemIndexScheduler : IModItemIndexScheduler
    {
        public event EventHandler<ModFastBatchAppliedEventArgs>? FastBatchApplied
        {
            add { }
            remove { }
        }
        public event EventHandler<ModEnrichmentAppliedEventArgs>? EnrichmentApplied
        {
            add { }
            remove { }
        }
        public event EventHandler? AllWorkCompleted
        {
            add { }
            remove { }
        }
        public bool IsFastPassRunning => false;
        public bool IsDeepPassRunning => false;

        public Task QueueRefreshAsync(
            ModIndexRefreshRequest request,
            IProgress<ModIndexRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpModPackageScanService : IModPackageScanService
    {
        public Task<IReadOnlyList<ModPackageScanResult>> ScanAsync(string modsRoot, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModPackageScanResult>>(Array.Empty<ModPackageScanResult>());
    }

    private sealed class NoOpModPackageInventoryService : IModPackageInventoryService
    {
        public Task<ModPackageInventoryRefreshResult> RefreshAsync(
            string modsRoot,
            IProgress<ModPackageInventoryRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ModPackageInventoryRefreshResult
            {
                Snapshot = new ModPackageInventorySnapshot
                {
                    ModsRootPath = modsRoot,
                    InventoryVersion = 1,
                    Entries = Array.Empty<ModPackageInventoryEntry>(),
                    LastValidatedUtcTicks = DateTime.UtcNow.Ticks
                }
            });
        }
    }

    private sealed class NoOpModItemInspectService : IModItemInspectService
    {
        public Task<ModItemInspectDetail?> TryGetAsync(string itemKey, CancellationToken cancellationToken = default)
            => Task.FromResult<ModItemInspectDetail?>(null);
    }

    private sealed class FakeTrayPreviewCoordinator : ITrayPreviewCoordinator
    {
        public bool NextPageFromCache { get; set; }

        public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
        {
            result = null!;
            return false;
        }

        public Task<TrayPreviewLoadResult> LoadAsync(TrayPreviewInput input, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewLoadResult
            {
                Summary = new SimsTrayPreviewSummary(),
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = 1,
                    PageSize = 50,
                    TotalItems = 0,
                    TotalPages = 1,
                    Items = Array.Empty<SimsTrayPreviewItem>()
                },
                LoadedPageCount = 1
            });
        }

        public Task<TrayPreviewPageResult> LoadPageAsync(int requestedPageIndex, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewPageResult
            {
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = 1,
                    PageSize = 50,
                    TotalItems = 0,
                    TotalPages = 1,
                    Items = Array.Empty<SimsTrayPreviewItem>()
                },
                LoadedPageCount = 1,
                FromCache = NextPageFromCache
            });
        }

        public void Invalidate(PreviewSourceRef? source = null)
        {
        }

        public void Reset()
        {
        }
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public IReadOnlyList<string> NextFolderPaths { get; set; } = Array.Empty<string>();

        public Task<IReadOnlyList<string>> PickFolderPathsAsync(string title, bool allowMultiple)
        {
            return Task.FromResult(NextFolderPaths);
        }

        public Task<string?> PickFilePathAsync(string title, string fileTypeName, IReadOnlyList<string> patterns)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickCsvSavePathAsync(string title, string suggestedFileName)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FakeConfirmationDialogService : IConfirmationDialogService
    {
        public bool NextResult { get; set; } = true;
        public int CallCount { get; private set; }

        public Task<bool> ConfirmAsync(ConfirmationRequest request)
        {
            CallCount++;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private AppSettings _settings;

        public FakeSettingsStore(AppSettings settings)
        {
            _settings = settings;
        }

        public AppSettings? LastSaved { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_settings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            LastSaved = settings;
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTrayDependencyExportService : ITrayDependencyExportService
    {
        public Func<TrayDependencyExportRequest, TrayDependencyExportResult>? OnExport { get; set; }
        public Func<TrayDependencyExportRequest, CancellationToken, Task<TrayDependencyExportResult>>? OnExportAsync { get; set; }

        public async Task<TrayDependencyExportResult> ExportAsync(
            TrayDependencyExportRequest request,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new TrayDependencyExportProgress
            {
                Stage = TrayDependencyExportStage.Preparing,
                Percent = 0,
                Detail = "Copying tray files..."
            });
            progress?.Report(new TrayDependencyExportProgress
            {
                Stage = TrayDependencyExportStage.IndexingPackages,
                Percent = 30,
                Detail = "Indexing packages..."
            });
            cancellationToken.ThrowIfCancellationRequested();

            var result = OnExportAsync is not null
                ? await OnExportAsync(request, cancellationToken)
                : OnExport?.Invoke(request) ?? new TrayDependencyExportResult
            {
                Success = true,
                CopiedTrayFileCount = request.TraySourceFiles.Count,
                CopiedModFileCount = 1
            };

            progress?.Report(new TrayDependencyExportProgress
            {
                Stage = TrayDependencyExportStage.Completed,
                Percent = 100,
                Detail = "Completed."
            });

            return result;
        }
    }

    private sealed class FakeTrayDependencyAnalysisService : ITrayDependencyAnalysisService
    {
        public int AnalyzeCount { get; private set; }
        public TrayDependencyAnalysisRequest? LastRequest { get; private set; }
        public Func<TrayDependencyAnalysisRequest, TrayDependencyAnalysisResult>? OnAnalyze { get; set; }

        public Task<TrayDependencyAnalysisResult> AnalyzeAsync(
            TrayDependencyAnalysisRequest request,
            IProgress<TrayDependencyAnalysisProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            AnalyzeCount++;
            LastRequest = request;
            progress?.Report(new TrayDependencyAnalysisProgress
            {
                Stage = TrayDependencyAnalysisStage.Preparing,
                Percent = 0,
                Detail = "Locating tray files..."
            });
            progress?.Report(new TrayDependencyAnalysisProgress
            {
                Stage = TrayDependencyAnalysisStage.IndexingPackages,
                Percent = 25,
                Detail = "Indexing packages..."
            });
            progress?.Report(new TrayDependencyAnalysisProgress
            {
                Stage = TrayDependencyAnalysisStage.Completed,
                Percent = 100,
                Detail = "Completed."
            });

            return Task.FromResult(OnAnalyze?.Invoke(request) ?? new TrayDependencyAnalysisResult
            {
                Success = true,
                MatchedPackageCount = 1
            });
        }
    }

    private sealed class FailingTrayThumbnailService : ITrayThumbnailService
    {
        public int CallCount { get; private set; }

        public Task<TrayThumbnailResult> GetThumbnailAsync(
            SimsTrayPreviewItem item,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new TrayThumbnailResult
            {
                SourceKind = TrayThumbnailSourceKind.Placeholder,
                Success = false
            });
        }

        public Task CleanupStaleEntriesAsync(
            string trayRootPath,
            IReadOnlyCollection<string> liveItemKeys,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void ResetMemoryCache(string? trayRootPath = null)
        {
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sims-tray-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

