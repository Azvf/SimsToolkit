namespace SimsModDesktop.PackageCore;

public static class Sims4ResourceTypeRegistry
{
    public const uint CasPart = 0x034AEECB;
    public const uint BuildBuyObject = 0x319E4F1D;
    public const uint StringTable = 0x220557DA;

    public static bool IsSupportedGameItemType(uint type)
    {
        return type == CasPart || type == BuildBuyObject;
    }

    public static bool IsStringTableType(uint type)
    {
        return type == StringTable;
    }

    public static string ResolveEntityKind(uint type)
    {
        return type == CasPart ? "Cas" : "BuildBuy";
    }

    public static string ResolveEntitySubType(uint type)
    {
        return type == CasPart ? "CAS Part" : "Object";
    }
}
