namespace SimsModDesktop.Application.Modules.Plugins;

public interface IExternalModuleProvider
{
    public const int ContractVersion = 1;

    IEnumerable<IActionModule> LoadModules();
}
