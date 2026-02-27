using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Modules;

public interface IActionModuleRegistry
{
    IReadOnlyList<IActionModule> All { get; }
    IActionModule Get(SimsAction action);
}
