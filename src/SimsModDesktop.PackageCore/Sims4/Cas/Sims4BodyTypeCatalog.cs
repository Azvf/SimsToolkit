namespace SimsModDesktop.PackageCore;

public static class Sims4BodyTypeCatalog
{
    private static readonly IReadOnlyDictionary<uint, string> Names = new Dictionary<uint, string>
    {
        [1] = "Hat",
        [2] = "Hair",
        [3] = "Head",
        [4] = "Face",
        [5] = "Body",
        [6] = "Top",
        [7] = "Bottom",
        [8] = "Shoes",
        [9] = "Accessories",
        [0x0A] = "Earrings",
        [0x0B] = "Glasses",
        [0x0C] = "Necklace",
        [0x0D] = "Gloves",
        [0x1C] = "FacialHair",
        [0x1D] = "Lipstick",
        [0x1E] = "Eyeshadow",
        [0x1F] = "Eyeliner",
        [0x20] = "Blush",
        [0x21] = "Facepaint",
        [0x22] = "Eyebrows",
        [0x23] = "Eyecolor",
        [0x24] = "Socks",
        [0x25] = "Mascara",
        [0x2A] = "Tights"
    };

    public static string ResolveDisplayName(uint bodyType) =>
        Names.TryGetValue(bodyType, out var name) ? name : "CAS Part";

    public static string ResolveSubTypeCode(uint bodyType) => ResolveDisplayName(bodyType);
}
