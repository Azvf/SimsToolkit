using SimsModDesktop.Application.Presets;

namespace SimsModDesktop.ViewModels;

public sealed class QuickPresetListItem
{
    public QuickPresetListItem(QuickPresetDefinition definition, bool isLastApplied)
    {
        Definition = definition;
        IsLastApplied = isLastApplied;
    }

    public QuickPresetDefinition Definition { get; }
    public bool IsLastApplied { get; }
    public string Name => Definition.Name;
    public string Description => Definition.Description;
}

