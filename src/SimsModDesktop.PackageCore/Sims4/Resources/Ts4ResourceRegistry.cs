using System.Collections.Frozen;

namespace SimsModDesktop.PackageCore;

public sealed class Ts4ResourceRegistry : ITS4ResourceRegistry
{
    private static readonly FrozenDictionary<Ts4ResourceKind, uint> KindToType =
        new Dictionary<Ts4ResourceKind, uint>
        {
            [Ts4ResourceKind.CasPart] = Sims4ResourceTypeRegistry.CasPart,
            [Ts4ResourceKind.BuildBuyObject] = Sims4ResourceTypeRegistry.BuildBuyObject,
            [Ts4ResourceKind.StringTable] = Sims4ResourceTypeRegistry.StringTable,
            [Ts4ResourceKind.SimInfo] = Sims4ResourceTypeRegistry.SimInfo,
            [Ts4ResourceKind.SimModifier] = Sims4ResourceTypeRegistry.SimModifier,
            [Ts4ResourceKind.Sculpt] = Sims4ResourceTypeRegistry.Sculpt,
            [Ts4ResourceKind.BlendGeometry] = Sims4ResourceTypeRegistry.BlendGeometry,
            [Ts4ResourceKind.DeformerMap] = Sims4ResourceTypeRegistry.DeformerMap,
            [Ts4ResourceKind.BoneDelta] = Sims4ResourceTypeRegistry.BoneDelta,
            [Ts4ResourceKind.Geom] = Sims4ResourceTypeRegistry.Geom,
            [Ts4ResourceKind.Rig] = Sims4ResourceTypeRegistry.Rig,
            [Ts4ResourceKind.Tone] = Sims4ResourceTypeRegistry.Tone,
            [Ts4ResourceKind.PeltLayer] = Sims4ResourceTypeRegistry.PeltLayer,
            [Ts4ResourceKind.RegionMap] = Sims4ResourceTypeRegistry.RegionMap,
            [Ts4ResourceKind.Rle2] = Sims4ResourceTypeRegistry.Rle2,
            [Ts4ResourceKind.Rles] = Sims4ResourceTypeRegistry.Rles,
            [Ts4ResourceKind.Lrle] = Sims4ResourceTypeRegistry.Lrle,
            [Ts4ResourceKind.Dds] = Sims4ResourceTypeRegistry.Dds,
            [Ts4ResourceKind.DdsUncompressed] = Sims4ResourceTypeRegistry.DdsUncompressed,
            [Ts4ResourceKind.Dst] = Sims4ResourceTypeRegistry.Dst,
            [Ts4ResourceKind.Tuning1] = Sims4ResourceTypeRegistry.Tuning1,
            [Ts4ResourceKind.Tuning2] = Sims4ResourceTypeRegistry.Tuning2
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<uint, Ts4ResourceKind> TypeToKind =
        KindToType.ToDictionary(pair => pair.Value, pair => pair.Key).ToFrozenDictionary();

    public bool TryGetTypeId(Ts4ResourceKind kind, out uint typeId)
    {
        return KindToType.TryGetValue(kind, out typeId);
    }

    public Ts4ResourceKind ResolveKind(uint typeId)
    {
        return TypeToKind.TryGetValue(typeId, out var kind)
            ? kind
            : Ts4ResourceKind.Unknown;
    }
}
