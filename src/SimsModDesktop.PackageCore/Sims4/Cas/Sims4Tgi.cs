namespace SimsModDesktop.PackageCore;

public readonly record struct Sims4Tgi(uint Type, uint Group, ulong Instance)
{
    public DbpfResourceKey ToResourceKey() => new(Type, Group, Instance);

    public string ToKeyText() => $"{Type:X8}:{Group:X8}:{Instance:X16}";

    public bool EqualsByTypeGroupInstance(Sims4Tgi other) =>
        Type == other.Type && Group == other.Group && Instance == other.Instance;
}
