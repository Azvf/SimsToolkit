using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Presentation.ViewModels.Shell;

public sealed class DebugConfigToggleItemViewModel : ObservableObject
{
    private bool _value;

    public DebugConfigToggleItemViewModel(
        string key,
        string displayName,
        string description,
        bool defaultValue)
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
        DefaultValue = defaultValue;
        _value = defaultValue;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool DefaultValue { get; }

    public bool Value
    {
        get => _value;
        set
        {
            if (!SetProperty(ref _value, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDefaultValue));
            OnPropertyChanged(nameof(DefaultValueText));
        }
    }

    public bool IsDefaultValue => Value == DefaultValue;
    public string DefaultValueText => DefaultValue ? "On" : "Off";

    public void ResetToDefault()
    {
        Value = DefaultValue;
    }
}
