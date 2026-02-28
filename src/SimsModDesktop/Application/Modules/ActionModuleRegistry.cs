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

        var modulesWithMissingKey = all
            .Where(module => string.IsNullOrWhiteSpace(module.ModuleKey))
            .Select(module => module.Action.ToString())
            .ToArray();
        if (modulesWithMissingKey.Length > 0)
        {
            throw new InvalidOperationException(
                "Action modules must provide a non-empty ModuleKey. Invalid actions: " +
                string.Join(", ", modulesWithMissingKey));
        }

        var duplicateActions = all
            .GroupBy(module => module.Action)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (duplicateActions.Length > 0)
        {
            throw new InvalidOperationException(
                "Duplicate action modules were registered: " + string.Join(", ", duplicateActions));
        }

        var duplicateModuleKeys = all
            .GroupBy(module => module.ModuleKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (duplicateModuleKeys.Length > 0)
        {
            throw new InvalidOperationException(
                "Duplicate module keys were registered: " + string.Join(", ", duplicateModuleKeys));
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
