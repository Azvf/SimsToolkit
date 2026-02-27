using Avalonia.Controls;

namespace SimsModDesktop.Infrastructure.Windowing;

public sealed class WindowHostService : IWindowHostService
{
    public TopLevel? CurrentTopLevel { get; set; }
}
