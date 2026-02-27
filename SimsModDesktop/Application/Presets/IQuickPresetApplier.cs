namespace SimsModDesktop.Application.Presets;

public interface IQuickPresetApplier
{
    bool TryApply(QuickPresetDefinition preset, out string error);
}
