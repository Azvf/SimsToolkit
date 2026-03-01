using System.Windows.Input;

namespace SimsModDesktop.ViewModels.Preview;

public sealed class PreviewSurfaceActionButtonViewModel
{
    public required string Label { get; init; }
    public required ICommand Command { get; init; }
    public bool IsPrimary { get; init; }
}
