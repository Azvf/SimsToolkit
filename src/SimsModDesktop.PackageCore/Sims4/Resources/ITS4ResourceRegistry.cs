namespace SimsModDesktop.PackageCore;

public interface ITS4ResourceRegistry
{
    bool TryGetTypeId(Ts4ResourceKind kind, out uint typeId);

    Ts4ResourceKind ResolveKind(uint typeId);
}
