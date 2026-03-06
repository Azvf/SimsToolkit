namespace SimsModDesktop.PackageCore;

public sealed class Ts4BondHeader
{
    public required uint ContextVersion { get; init; }
    public required uint Version { get; init; }
    public required uint BoneAdjustCount { get; init; }
}

public sealed class Ts4BondHeaderParser : ITS4ResourceParser<Ts4BondHeader>
{
    private readonly Ts4BondParser _bondParser = new();

    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4BondHeader result, out string? error)
    {
        result = null!;
        error = null;

        if (_bondParser.TryParse(key, bytes, out var bond, out error))
        {
            result = new Ts4BondHeader
            {
                ContextVersion = bond.ContextVersion,
                Version = bond.Version,
                BoneAdjustCount = (uint)bond.Adjustments.Count
            };
            return true;
        }

        return false;
    }
}
