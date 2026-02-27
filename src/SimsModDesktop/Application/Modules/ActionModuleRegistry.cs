using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Modules;

public sealed class ActionModuleRegistry : IActionModuleRegistry
{
    private readonly IReadOnlyList<IActionModule> _all;
    private readonly IReadOnlyDictionary<SimsAction, IActionModule> _byAction;

    public ActionModuleRegistry(IEnumerable<IActionModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var all = modules.ToList();
        if (all.Count == 0)
        {
            throw new InvalidOperationException("No action modules were registered.");
        }

        _all = all;
        _byAction = all.ToDictionary(module => module.Action);
    }

    public IReadOnlyList<IActionModule> All => _all;

    public IActionModule Get(SimsAction action)
    {
        if (_byAction.TryGetValue(action, out var module))
        {
            return module;
        }

        throw new InvalidOperationException($"Action module is not registered: {action}.");
    }
}
