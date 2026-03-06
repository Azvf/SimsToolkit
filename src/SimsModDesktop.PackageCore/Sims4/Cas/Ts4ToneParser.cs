namespace SimsModDesktop.PackageCore;

public enum Ts4ToneSkinPanel : ushort
{
    Unknown = 0,
    Warm = 1,
    Neutral = 2,
    Cool = 3,
    Miscellaneous = 4
}

public readonly record struct Ts4ToneSkinSet(
    ulong TextureInstance,
    ulong OverlayInstance,
    float OverlayMultiplier,
    float MakeupOpacity,
    float MakeupOpacity2);

public readonly record struct Ts4ToneOverlay(uint AgeGenderFlags, ulong TextureInstance);

public readonly record struct Ts4ToneCategoryTag(ushort Category, uint Value);

public readonly record struct Ts4ToneColor(int Argb);

public sealed class Ts4Tone
{
    public required uint Version { get; init; }
    public required IReadOnlyList<Ts4ToneSkinSet> SkinSets { get; init; }
    public required IReadOnlyList<Ts4ToneOverlay> Overlays { get; init; }
    public required ushort Saturation { get; init; }
    public required ushort Hue { get; init; }
    public required uint Opacity { get; init; }
    public required IReadOnlyList<Ts4ToneCategoryTag> CategoryTags { get; init; }
    public required IReadOnlyList<Ts4ToneColor> Colors { get; init; }
    public required float SortOrder { get; init; }
    public required ulong TuningInstance { get; init; }
    public required Ts4ToneSkinPanel SkinPanel { get; init; }
    public required float SliderLow { get; init; }
    public required float SliderHigh { get; init; }
    public required float SliderIncrement { get; init; }
}

public sealed class Ts4ToneParser : ITS4ResourceParser<Ts4Tone>
{
    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4Tone result, out string? error)
    {
        result = null!;
        error = null;

        if (key.Type != Sims4ResourceTypeRegistry.Tone)
        {
            error = "Resource is not TONE.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var version = reader.ReadUInt32();

            var skinSets = new List<Ts4ToneSkinSet>();
            ulong legacyTextureInstance = 0;
            if (version >= 10)
            {
                var skinSetCount = reader.ReadByte();
                for (var i = 0; i < skinSetCount; i++)
                {
                    skinSets.Add(new Ts4ToneSkinSet(
                        reader.ReadUInt64(),
                        reader.ReadUInt64(),
                        reader.ReadSingle(),
                        reader.ReadSingle(),
                        reader.ReadSingle()));
                }
            }
            else
            {
                legacyTextureInstance = reader.ReadUInt64();
            }

            var overlayCount = reader.ReadInt32();
            if (overlayCount < 0)
            {
                throw new InvalidDataException("Invalid TONE overlay count.");
            }

            var overlays = new List<Ts4ToneOverlay>(overlayCount);
            for (var i = 0; i < overlayCount; i++)
            {
                overlays.Add(new Ts4ToneOverlay(reader.ReadUInt32(), reader.ReadUInt64()));
            }

            var saturation = reader.ReadUInt16();
            var hue = reader.ReadUInt16();
            var opacity = reader.ReadUInt32();

            var tagCount = reader.ReadInt32();
            if (tagCount < 0)
            {
                throw new InvalidDataException("Invalid TONE tag count.");
            }

            var tags = new List<Ts4ToneCategoryTag>(tagCount);
            for (var i = 0; i < tagCount; i++)
            {
                var category = reader.ReadUInt16();
                var value = version >= 7
                    ? reader.ReadUInt32()
                    : reader.ReadUInt16();
                tags.Add(new Ts4ToneCategoryTag(category, value));
            }

            var legacyMakeupOpacity = version < 10 ? reader.ReadSingle() : 0.5f;

            var colorCount = reader.ReadByte();
            var colors = new List<Ts4ToneColor>(colorCount);
            for (var i = 0; i < colorCount; i++)
            {
                colors.Add(new Ts4ToneColor(reader.ReadInt32()));
            }

            var sortOrder = reader.ReadSingle();
            var legacyMakeupOpacity2 = version < 10 ? reader.ReadSingle() : 0.5f;
            var tuningInstance = version >= 8 ? reader.ReadUInt64() : 0;

            var skinPanel = Ts4ToneSkinPanel.Unknown;
            var sliderLow = -0.05f;
            var sliderHigh = 0.05f;
            var sliderIncrement = 0.005f;
            if (version > 10)
            {
                skinPanel = (Ts4ToneSkinPanel)reader.ReadUInt16();
                sliderLow = reader.ReadSingle();
                sliderHigh = reader.ReadSingle();
                sliderIncrement = reader.ReadSingle();
            }

            if (version < 10)
            {
                skinSets.Add(new Ts4ToneSkinSet(
                    legacyTextureInstance,
                    0,
                    1f,
                    legacyMakeupOpacity,
                    legacyMakeupOpacity2));
            }

            result = new Ts4Tone
            {
                Version = version,
                SkinSets = skinSets,
                Overlays = overlays,
                Saturation = saturation,
                Hue = hue,
                Opacity = opacity,
                CategoryTags = tags,
                Colors = colors,
                SortOrder = sortOrder,
                TuningInstance = tuningInstance,
                SkinPanel = skinPanel,
                SliderLow = sliderLow,
                SliderHigh = sliderHigh,
                SliderIncrement = sliderIncrement
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse TONE: {ex.Message}";
            return false;
        }
    }
}
