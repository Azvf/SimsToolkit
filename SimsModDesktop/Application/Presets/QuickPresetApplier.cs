using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.Application.Presets;

public sealed class QuickPresetApplier : IQuickPresetApplier
{
    private readonly IActionModuleRegistry _moduleRegistry;
    private readonly SharedFileOpsPanelViewModel _sharedFileOps;

    public QuickPresetApplier(
        IActionModuleRegistry moduleRegistry,
        SharedFileOpsPanelViewModel sharedFileOps)
    {
        _moduleRegistry = moduleRegistry;
        _sharedFileOps = sharedFileOps;
    }

    public bool TryApply(QuickPresetDefinition preset, out string error)
    {
        error = string.Empty;

        IActionModule module;
        try
        {
            module = _moduleRegistry.Get(preset.Action);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!module.TryApplyActionPatch(preset.ActionPatch, out error))
        {
            return false;
        }

        if (preset.SharedPatch is not null && !TryApplySharedPatch(preset, out error))
        {
            return false;
        }

        return true;
    }

    private bool TryApplySharedPatch(QuickPresetDefinition preset, out string error)
    {
        error = string.Empty;
        var patch = preset.SharedPatch!;

        if (!ModuleHelpers.TryGetBoolean(patch, "skipPruneEmptyDirs", out var hasSkipPruneEmptyDirs, out var skipPruneEmptyDirs, out error) ||
            !ModuleHelpers.TryGetBoolean(patch, "modFilesOnly", out var hasModFilesOnly, out var modFilesOnly, out error) ||
            !ModuleHelpers.TryGetBoolean(patch, "verifyContentOnNameConflict", out var hasVerifyContentOnNameConflict, out var verifyContentOnNameConflict, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetInt32(patch, "prefixHashBytes", out var hasPrefixHashBytes, out var prefixHashBytes, out error) ||
            !ModuleHelpers.TryGetInt32(patch, "hashWorkerCount", out var hasHashWorkerCount, out var hashWorkerCount, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetStringList(patch, "modExtensions", out var hasModExtensions, out var modExtensions, out error))
        {
            return false;
        }

        if (hasSkipPruneEmptyDirs)
        {
            _sharedFileOps.SkipPruneEmptyDirs = skipPruneEmptyDirs;
        }

        if (hasModFilesOnly)
        {
            _sharedFileOps.ModFilesOnly = modFilesOnly;
        }

        if (hasVerifyContentOnNameConflict)
        {
            _sharedFileOps.VerifyContentOnNameConflict = verifyContentOnNameConflict;
        }

        if (hasPrefixHashBytes)
        {
            _sharedFileOps.PrefixHashBytesText = prefixHashBytes.ToString();
        }

        if (hasHashWorkerCount)
        {
            _sharedFileOps.HashWorkerCountText = hashWorkerCount.ToString();
        }

        if (hasModExtensions)
        {
            _sharedFileOps.ModExtensionsText = string.Join(",", modExtensions);
        }

        if (!InputParsing.TryParseOptionalInt(_sharedFileOps.PrefixHashBytesText, 1024, 104857600, out _, out error))
        {
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(_sharedFileOps.HashWorkerCountText, 1, 64, out _, out error))
        {
            return false;
        }

        return true;
    }
}
