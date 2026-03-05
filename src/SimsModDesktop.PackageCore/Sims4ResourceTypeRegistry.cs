namespace SimsModDesktop.PackageCore;

public static class Sims4ResourceTypeRegistry
{
    public const uint CasPart = 0x034AEECB;
    public const uint BuildBuyObject = 0x319E4F1D;
    public const uint StringTable = 0x220557DA;
    public const uint SimInfo = 0x545AC67A;
    public const uint SimModifier = 0xC5F6763E;
    public const uint Sculpt = 0x9D1AB874;
    public const uint BlendGeometry = 0x067CAA11;
    public const uint DeformerMap = 0xDB43E069;
    public const uint BoneDelta = 0x0355E0A6;
    public const uint Geom = 0x015A1849;
    public const uint Rig = 0x8EAF13DE;
    public const uint Tone = 0x0354796A;
    public const uint PeltLayer = 0x26AF8338;
    public const uint RegionMap = 0xAC16FBEC;
    public const uint Rle2 = 0x3453CF95;
    public const uint Rles = 0xBA856C78;
    public const uint Lrle = 0x2BC04EDF;
    public const uint Dds = 0x00B2D882;
    public const uint DdsUncompressed = 0xB6C8B6A0;
    public const uint Dst = 0x2F7D0004;
    public const uint Tuning1 = 0xF3ABFF3C;
    public const uint Tuning2 = 0x03B33DDF;

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

    public static bool IsTextureType(uint type)
    {
        return type == Dds ||
               type == DdsUncompressed ||
               type == Rle2 ||
               type == Rles ||
               type == Lrle ||
               type == Dst;
    }
}
