using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Application.Execution;

public sealed class ToolkitActionPlanner : IToolkitActionPlanner
{
    private static readonly IReadOnlyList<SimsAction> SupportedActions =
    [
        SimsAction.Organize,
        SimsAction.TextureCompress,
        SimsAction.Flatten,
        SimsAction.Normalize,
        SimsAction.Merge,
        SimsAction.FindDuplicates,
        SimsAction.TrayDependencies
    ];

    private readonly IOrganizeModuleState _organize;
    private readonly ITextureCompressModuleState _textureCompress;
    private readonly IFlattenModuleState _flatten;
    private readonly INormalizeModuleState _normalize;
    private readonly IMergeModuleState _merge;
    private readonly IFindDupModuleState _findDup;
    private readonly ITrayDependenciesModuleState _trayDependencies;
    private readonly ITrayPreviewModuleState _trayPreview;

    public ToolkitActionPlanner(
        IOrganizeModuleState organize,
        ITextureCompressModuleState textureCompress,
        IFlattenModuleState flatten,
        INormalizeModuleState normalize,
        IMergeModuleState merge,
        IFindDupModuleState findDup,
        ITrayDependenciesModuleState trayDependencies,
        ITrayPreviewModuleState trayPreview)
    {
        _organize = organize;
        _textureCompress = textureCompress;
        _flatten = flatten;
        _normalize = normalize;
        _merge = merge;
        _findDup = findDup;
        _trayDependencies = trayDependencies;
        _trayPreview = trayPreview;
    }

    public IReadOnlyList<SimsAction> AvailableToolkitActions => SupportedActions;

    public bool UsesSharedFileOps(SimsAction action)
    {
        return action is SimsAction.Flatten or SimsAction.Merge or SimsAction.FindDuplicates;
    }

    public string GetDisplayName(SimsAction action)
    {
        return action switch
        {
            SimsAction.Organize => "Organize",
            SimsAction.TextureCompress => "Texture Compress",
            SimsAction.Flatten => "Flatten",
            SimsAction.Normalize => "Normalize",
            SimsAction.Merge => "Merge",
            SimsAction.FindDuplicates => "FindDuplicates",
            SimsAction.TrayDependencies => "Tray Dependencies",
            _ => action.ToString()
        };
    }

    public void LoadModuleSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _organize.SourceDir = settings.Organize.SourceDir;
        _organize.ZipNamePattern = settings.Organize.ZipNamePattern;
        _organize.ModsRoot = settings.Organize.ModsRoot;
        _organize.UnifiedModsFolder = settings.Organize.UnifiedModsFolder;
        _organize.TrayRoot = settings.Organize.TrayRoot;
        _organize.KeepZip = settings.Organize.KeepZip;

        _textureCompress.SourcePath = settings.TextureCompress.SourcePath;
        _textureCompress.OutputPath = settings.TextureCompress.OutputPath;
        _textureCompress.TargetWidthText = settings.TextureCompress.TargetWidthText;
        _textureCompress.TargetHeightText = settings.TextureCompress.TargetHeightText;
        _textureCompress.HasAlphaHint = settings.TextureCompress.HasAlphaHint;
        _textureCompress.GenerateMipMaps = settings.TextureCompress.GenerateMipMaps;
        _textureCompress.PreferredFormat = NormalizePreferredFormat(settings.TextureCompress.PreferredFormat);

        _flatten.RootPath = settings.Flatten.RootPath;
        _flatten.FlattenToRoot = settings.Flatten.FlattenToRoot;

        _normalize.RootPath = settings.Normalize.RootPath;

        _merge.ApplySourcePathsText(settings.Merge.SourcePathsText);
        _merge.TargetPath = settings.Merge.TargetPath;

        _findDup.RootPath = settings.FindDup.RootPath;
        _findDup.OutputCsv = settings.FindDup.OutputCsv;
        _findDup.Recurse = settings.FindDup.Recurse;
        _findDup.Cleanup = settings.FindDup.Cleanup;

        _trayDependencies.TrayItemKey = settings.TrayDependencies.TrayItemKey;
        _trayDependencies.MinMatchCountText = settings.TrayDependencies.MinMatchCountText;
        _trayDependencies.TopNText = settings.TrayDependencies.TopNText;
        _trayDependencies.MaxPackageCountText = settings.TrayDependencies.MaxPackageCountText;
        _trayDependencies.ExportUnusedPackages = settings.TrayDependencies.ExportUnusedPackages;
        _trayDependencies.ExportMatchedPackages = settings.TrayDependencies.ExportMatchedPackages;
        _trayDependencies.OutputCsv = settings.TrayDependencies.OutputCsv;
        _trayDependencies.UnusedOutputCsv = settings.TrayDependencies.UnusedOutputCsv;
        _trayDependencies.ExportTargetPath = settings.TrayDependencies.ExportTargetPath;
        _trayDependencies.ExportMinConfidence = settings.TrayDependencies.ExportMinConfidence;

        _trayPreview.PresetTypeFilter = NormalizeFilter(settings.TrayPreview.PresetTypeFilter);
        _trayPreview.BuildSizeFilter = NormalizeFilter(settings.TrayPreview.BuildSizeFilter);
        _trayPreview.HouseholdSizeFilter = NormalizeFilter(settings.TrayPreview.HouseholdSizeFilter);
        _trayPreview.AuthorFilter = settings.TrayPreview.AuthorFilter;
        _trayPreview.TimeFilter = NormalizeFilter(settings.TrayPreview.TimeFilter);
        _trayPreview.SearchQuery = settings.TrayPreview.SearchQuery;
        _trayPreview.LayoutMode = NormalizeLayoutMode(settings.TrayPreview.LayoutMode);
        _trayPreview.EnableDebugPreview = settings.TrayPreview.EnableDebugPreview;
    }

    public void SaveModuleSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Organize.SourceDir = _organize.SourceDir;
        settings.Organize.ZipNamePattern = _organize.ZipNamePattern;
        settings.Organize.ModsRoot = _organize.ModsRoot;
        settings.Organize.UnifiedModsFolder = _organize.UnifiedModsFolder;
        settings.Organize.TrayRoot = _organize.TrayRoot;
        settings.Organize.KeepZip = _organize.KeepZip;

        settings.TextureCompress.SourcePath = _textureCompress.SourcePath;
        settings.TextureCompress.OutputPath = _textureCompress.OutputPath;
        settings.TextureCompress.TargetWidthText = _textureCompress.TargetWidthText;
        settings.TextureCompress.TargetHeightText = _textureCompress.TargetHeightText;
        settings.TextureCompress.HasAlphaHint = _textureCompress.HasAlphaHint;
        settings.TextureCompress.GenerateMipMaps = _textureCompress.GenerateMipMaps;
        settings.TextureCompress.PreferredFormat = NormalizePreferredFormat(_textureCompress.PreferredFormat);

        settings.Flatten.RootPath = _flatten.RootPath;
        settings.Flatten.FlattenToRoot = _flatten.FlattenToRoot;

        settings.Normalize.RootPath = _normalize.RootPath;

        settings.Merge.SourcePathsText = _merge.SerializeSourcePaths();
        settings.Merge.TargetPath = _merge.TargetPath;

        settings.FindDup.RootPath = _findDup.RootPath;
        settings.FindDup.OutputCsv = _findDup.OutputCsv;
        settings.FindDup.Recurse = _findDup.Recurse;
        settings.FindDup.Cleanup = _findDup.Cleanup;

        settings.TrayDependencies.TrayItemKey = _trayDependencies.TrayItemKey;
        settings.TrayDependencies.MinMatchCountText = _trayDependencies.MinMatchCountText;
        settings.TrayDependencies.TopNText = _trayDependencies.TopNText;
        settings.TrayDependencies.MaxPackageCountText = _trayDependencies.MaxPackageCountText;
        settings.TrayDependencies.ExportUnusedPackages = _trayDependencies.ExportUnusedPackages;
        settings.TrayDependencies.ExportMatchedPackages = _trayDependencies.ExportMatchedPackages;
        settings.TrayDependencies.OutputCsv = _trayDependencies.OutputCsv;
        settings.TrayDependencies.UnusedOutputCsv = _trayDependencies.UnusedOutputCsv;
        settings.TrayDependencies.ExportTargetPath = _trayDependencies.ExportTargetPath;
        settings.TrayDependencies.ExportMinConfidence = _trayDependencies.ExportMinConfidence;

        settings.TrayPreview.PresetTypeFilter = NormalizeFilter(_trayPreview.PresetTypeFilter);
        settings.TrayPreview.BuildSizeFilter = NormalizeFilter(_trayPreview.BuildSizeFilter);
        settings.TrayPreview.HouseholdSizeFilter = NormalizeFilter(_trayPreview.HouseholdSizeFilter);
        settings.TrayPreview.AuthorFilter = _trayPreview.AuthorFilter;
        settings.TrayPreview.TimeFilter = NormalizeFilter(_trayPreview.TimeFilter);
        settings.TrayPreview.SearchQuery = _trayPreview.SearchQuery;
        settings.TrayPreview.LayoutMode = NormalizeLayoutMode(_trayPreview.LayoutMode);
        settings.TrayPreview.EnableDebugPreview = _trayPreview.EnableDebugPreview;
    }

    public bool TryBuildToolkitCliPlan(
        ToolkitPlanningState state,
        out CliExecutionPlan plan,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(state);

        plan = null!;

        if (!TryBuildGlobalExecutionOptions(
                state,
                includeShared: UsesSharedFileOps(state.SelectedAction),
                out var options,
                out error))
        {
            return false;
        }

        ISimsExecutionInput? input = state.SelectedAction switch
        {
            SimsAction.Organize => new OrganizeInput
            {
                WhatIf = options.WhatIf,
                SourceDir = ModuleHelpers.ToNullIfWhiteSpace(_organize.SourceDir),
                ZipNamePattern = ModuleHelpers.ToNullIfWhiteSpace(_organize.ZipNamePattern),
                ModsRoot = ModuleHelpers.ToNullIfWhiteSpace(_organize.ModsRoot),
                UnifiedModsFolder = ModuleHelpers.ToNullIfWhiteSpace(_organize.UnifiedModsFolder),
                TrayRoot = ModuleHelpers.ToNullIfWhiteSpace(_organize.TrayRoot),
                KeepZip = _organize.KeepZip
            },
            SimsAction.Flatten => new FlattenInput
            {
                WhatIf = options.WhatIf,
                FlattenRootPath = ModuleHelpers.ToNullIfWhiteSpace(_flatten.RootPath),
                FlattenToRoot = _flatten.FlattenToRoot,
                Shared = options.Shared
            },
            SimsAction.Normalize => new NormalizeInput
            {
                WhatIf = options.WhatIf,
                NormalizeRootPath = ModuleHelpers.ToNullIfWhiteSpace(_normalize.RootPath)
            },
            SimsAction.Merge => new MergeInput
            {
                WhatIf = options.WhatIf,
                MergeSourcePaths = _merge.CollectSourcePaths(),
                MergeTargetPath = ModuleHelpers.ToNullIfWhiteSpace(_merge.TargetPath),
                Shared = options.Shared
            },
            SimsAction.FindDuplicates => new FindDupInput
            {
                WhatIf = options.WhatIf,
                FindDupRootPath = ModuleHelpers.ToNullIfWhiteSpace(_findDup.RootPath),
                FindDupOutputCsv = ModuleHelpers.ToNullIfWhiteSpace(_findDup.OutputCsv),
                FindDupRecurse = _findDup.Recurse,
                FindDupCleanup = _findDup.Cleanup,
                Shared = options.Shared
            },
            _ => null
        };

        if (input is null)
        {
            error = $"Action {state.SelectedAction} is not a CLI action.";
            return false;
        }

        plan = new CliExecutionPlan(input);
        error = string.Empty;
        return true;
    }

    public bool TryBuildTrayDependenciesPlan(
        ToolkitPlanningState state,
        out TrayDependenciesExecutionPlan plan,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(state);

        _ = state;
        plan = null!;

        var trayPath = ModuleHelpers.ToNullIfWhiteSpace(_trayDependencies.TrayPath);
        if (string.IsNullOrWhiteSpace(trayPath))
        {
            error = "TrayPath is required for tray dependency analysis.";
            return false;
        }

        if (!Directory.Exists(trayPath))
        {
            error = "TrayPath does not exist for tray dependency analysis.";
            return false;
        }

        var modsPath = ModuleHelpers.ToNullIfWhiteSpace(_trayDependencies.ModsPath);
        if (string.IsNullOrWhiteSpace(modsPath))
        {
            error = "ModsPath is required for tray dependency analysis.";
            return false;
        }

        if (!Directory.Exists(modsPath))
        {
            error = "ModsPath does not exist for tray dependency analysis.";
            return false;
        }

        var trayItemKey = ModuleHelpers.ToNullIfWhiteSpace(_trayDependencies.TrayItemKey);
        if (string.IsNullOrWhiteSpace(trayItemKey))
        {
            error = "TrayItemKey is required for tray dependency analysis.";
            return false;
        }

        var exportTargetPath = ModuleHelpers.ToNullIfWhiteSpace(_trayDependencies.ExportTargetPath);
        if ((_trayDependencies.ExportMatchedPackages || _trayDependencies.ExportUnusedPackages) &&
            string.IsNullOrWhiteSpace(exportTargetPath))
        {
            error = "ExportTargetPath is required when exporting dependency packages.";
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(_trayDependencies.MinMatchCountText, 1, 1000, out var minMatchCount, out error) ||
            !InputParsing.TryParseOptionalInt(_trayDependencies.TopNText, 1, 10000, out var topN, out error) ||
            !InputParsing.TryParseOptionalInt(_trayDependencies.MaxPackageCountText, 0, 1000000, out var maxPackageCount, out error))
        {
            return false;
        }

        plan = new TrayDependenciesExecutionPlan(new TrayDependencyAnalysisRequest
        {
            TrayPath = trayPath,
            ModsRootPath = modsPath,
            TrayItemKey = trayItemKey,
            MinMatchCount = minMatchCount,
            TopN = topN,
            MaxPackageCount = maxPackageCount,
            ExportUnusedPackages = _trayDependencies.ExportUnusedPackages,
            ExportMatchedPackages = _trayDependencies.ExportMatchedPackages,
            OutputCsv = ModuleHelpers.ToNullIfWhiteSpace(_trayDependencies.OutputCsv),
            UnusedOutputCsv = ModuleHelpers.ToNullIfWhiteSpace(_trayDependencies.UnusedOutputCsv),
            ExportTargetPath = exportTargetPath,
            ExportMinConfidence = ModuleHelpers.ToNullIfWhiteSpace(_trayDependencies.ExportMinConfidence) ?? "Low"
        });
        error = string.Empty;
        return true;
    }

    public bool TryBuildTrayPreviewInput(
        ToolkitPlanningState state,
        out TrayPreviewInput input,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(state);

        _ = state;
        input = null!;

        var trayPath = _trayPreview.TrayRoot.Trim();
        if (string.IsNullOrWhiteSpace(trayPath))
        {
            error = "TrayPath is required for tray preview.";
            return false;
        }

        input = new TrayPreviewInput
        {
            TrayPath = Path.GetFullPath(trayPath),
            PageSize = 50,
            PresetTypeFilter = NormalizeFilter(_trayPreview.PresetTypeFilter),
            BuildSizeFilter = NormalizeFilter(_trayPreview.BuildSizeFilter),
            HouseholdSizeFilter = NormalizeFilter(_trayPreview.HouseholdSizeFilter),
            AuthorFilter = _trayPreview.AuthorFilter.Trim(),
            TimeFilter = NormalizeFilter(_trayPreview.TimeFilter),
            SearchQuery = _trayPreview.SearchQuery.Trim()
        };
        error = string.Empty;
        return true;
    }

    public bool TryBuildTextureCompressionPlan(
        ToolkitPlanningState state,
        out TextureCompressionExecutionPlan plan,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(state);

        plan = null!;

        var sourcePath = ModuleHelpers.ToNullIfWhiteSpace(_textureCompress.SourcePath);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            error = "SourcePath is required for texture compression.";
            return false;
        }

        sourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourcePath))
        {
            error = "SourcePath does not exist for texture compression.";
            return false;
        }

        var outputPath = ResolveOutputPath(sourcePath, _textureCompress.OutputPath);
        if (string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            error = "OutputPath must be different from SourcePath.";
            return false;
        }

        if (!TryParseOptionalDimension(_textureCompress.TargetWidthText, "TargetWidth", out var targetWidth, out error) ||
            !TryParseOptionalDimension(_textureCompress.TargetHeightText, "TargetHeight", out var targetHeight, out error))
        {
            return false;
        }

        if ((targetWidth.HasValue && !targetHeight.HasValue) || (!targetWidth.HasValue && targetHeight.HasValue))
        {
            error = "TargetWidth and TargetHeight must both be set, or both be left blank.";
            return false;
        }

        if (!TryParsePreferredFormat(_textureCompress.PreferredFormat, out var preferredFormat, out error))
        {
            return false;
        }

        plan = new TextureCompressionExecutionPlan(new TextureCompressionFileRequest
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            TargetWidth = targetWidth,
            TargetHeight = targetHeight,
            HasAlphaHint = _textureCompress.HasAlphaHint,
            GenerateMipMaps = _textureCompress.GenerateMipMaps,
            PreferredFormat = preferredFormat,
            WhatIf = state.WhatIf
        });

        return true;
    }

    private static bool TryBuildGlobalExecutionOptions(
        ToolkitPlanningState state,
        bool includeShared,
        out GlobalExecutionOptions options,
        out string error)
    {
        options = null!;
        error = string.Empty;

        SharedFileOpsInput shared;
        if (includeShared)
        {
            if (!TryBuildSharedFileOpsInput(state.SharedFileOps, out shared, out error))
            {
                return false;
            }
        }
        else
        {
            shared = new SharedFileOpsInput();
        }

        options = new GlobalExecutionOptions
        {
            WhatIf = state.WhatIf,
            Shared = shared
        };
        return true;
    }

    private static bool TryBuildSharedFileOpsInput(SharedFileOpsPlanState state, out SharedFileOpsInput input, out string error)
    {
        input = null!;
        error = string.Empty;

        if (!InputParsing.TryParseOptionalInt(state.PrefixHashBytesText, 1024, 104857600, out var prefixHashBytes, out error))
        {
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(state.HashWorkerCountText, 1, 64, out var hashWorkerCount, out error))
        {
            return false;
        }

        input = new SharedFileOpsInput
        {
            SkipPruneEmptyDirs = state.SkipPruneEmptyDirs,
            ModFilesOnly = state.ModFilesOnly,
            ModExtensions = InputParsing.ParseDelimitedList(state.ModExtensionsText),
            VerifyContentOnNameConflict = state.VerifyContentOnNameConflict,
            PrefixHashBytes = prefixHashBytes,
            HashWorkerCount = hashWorkerCount
        };

        return true;
    }

    private static string NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "All"
            : value.Trim();
    }

    private static string NormalizeLayoutMode(string? value)
    {
        return string.Equals(value, "Grid", StringComparison.OrdinalIgnoreCase)
            ? "Grid"
            : "Entry";
    }

    private static string ResolveOutputPath(string sourcePath, string? rawOutputPath)
    {
        var outputPath = ModuleHelpers.ToNullIfWhiteSpace(rawOutputPath);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        var directory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var extension = Path.GetExtension(sourcePath);
        var suffix = string.Equals(extension, ".dds", StringComparison.OrdinalIgnoreCase)
            ? ".compressed.dds"
            : ".dds";
        var fileName = Path.GetFileNameWithoutExtension(sourcePath) + suffix;
        return Path.Combine(directory, fileName);
    }

    private static bool TryParseOptionalDimension(string rawValue, string fieldName, out int? value, out string error)
    {
        error = string.Empty;
        value = null;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!int.TryParse(rawValue.Trim(), out var parsed) || parsed < 1 || parsed > 16384)
        {
            error = $"{fieldName} must be an integer between 1 and 16384.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParsePreferredFormat(string? rawValue, out TextureTargetFormat? value, out string error)
    {
        error = string.Empty;
        value = NormalizePreferredFormat(rawValue) switch
        {
            "Auto" => null,
            "BC1" => TextureTargetFormat.Bc1,
            "BC3" => TextureTargetFormat.Bc3,
            _ => null
        };

        if (value is null && !string.Equals(NormalizePreferredFormat(rawValue), "Auto", StringComparison.OrdinalIgnoreCase))
        {
            error = "PreferredFormat must be Auto, BC1, or BC3.";
            return false;
        }

        return true;
    }

    private static string NormalizePreferredFormat(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "BC1" => "BC1",
            "BC3" => "BC3",
            _ => "Auto"
        };
    }
}
