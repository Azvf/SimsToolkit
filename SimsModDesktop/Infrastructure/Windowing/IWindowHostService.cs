using Avalonia.Controls;

namespace SimsModDesktop.Infrastructure.Windowing;

public interface IWindowHostService
{
    TopLevel? CurrentTopLevel { get; set; }
}
