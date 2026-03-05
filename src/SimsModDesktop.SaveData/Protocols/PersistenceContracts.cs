using ProtoBuf;

namespace EA.Sims4
{
    [ProtoContract]
    public sealed class IdList : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1, DataFormat = DataFormat.FixedSize)]
        public ulong[]? ids { get; set; }
    }
}

namespace EA.Sims4.Persistence
{
    [ProtoContract]
    public sealed class SaveGameData : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(2)]
        public SaveSlotData? save_slot { get; set; }

        [ProtoMember(5)]
        public List<HouseholdData> households { get; } = new();

        [ProtoMember(6)]
        public List<SimData> sims { get; } = new();

        [ProtoMember(7)]
        public List<ZoneData> zones { get; } = new();
    }

    [ProtoContract]
    public sealed class SaveSlotData : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(9)]
        public string slot_name { get; set; } = string.Empty;
    }

    [ProtoContract]
    public sealed class HouseholdData : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(2, DataFormat = DataFormat.FixedSize, IsRequired = true)]
        public ulong household_id { get; set; }

        [ProtoMember(3)]
        public string name { get; set; } = string.Empty;

        [ProtoMember(4, DataFormat = DataFormat.FixedSize)]
        public ulong home_zone { get; set; }

        [ProtoMember(5)]
        public ulong money { get; set; }

        [ProtoMember(11)]
        public global::EA.Sims4.IdList? sims { get; set; }

        [ProtoMember(18)]
        public string description { get; set; } = string.Empty;
    }

    [ProtoContract]
    public sealed class SimData : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1, DataFormat = DataFormat.FixedSize, IsRequired = true)]
        public ulong sim_id { get; set; }

        [ProtoMember(4, DataFormat = DataFormat.FixedSize)]
        public ulong household_id { get; set; }

        [ProtoMember(5)]
        public string first_name { get; set; } = string.Empty;

        [ProtoMember(6)]
        public string last_name { get; set; } = string.Empty;

        [ProtoMember(7)]
        public uint gender { get; set; }

        [ProtoMember(8)]
        public uint age { get; set; }

        [ProtoMember(22)]
        public string household_name { get; set; } = string.Empty;

        [ProtoMember(60)]
        public uint extended_species { get; set; }

        [ProtoMember(9)]
        public float voice_pitch { get; set; }

        [ProtoMember(10, DataFormat = DataFormat.FixedSize)]
        public ulong skin_tone { get; set; }

        [ProtoMember(11)]
        public uint voice_actor { get; set; }

        [ProtoMember(12)]
        public string physique { get; set; } = string.Empty;

        [ProtoMember(18)]
        public byte[] facial_attr { get; set; } = Array.Empty<byte>();

        [ProtoMember(21)]
        public OutfitList? outfits { get; set; }

        [ProtoMember(28)]
        public GeneticData? genetic_data { get; set; }

        [ProtoMember(29)]
        public uint flags { get; set; }

        [ProtoMember(30)]
        public PersistableSimInfoAttributes? attributes { get; set; }

        [ProtoMember(34, DataFormat = DataFormat.FixedSize)]
        public ulong primary_aspiration { get; set; }

        [ProtoMember(38)]
        public uint current_outfit_type { get; set; }

        [ProtoMember(39)]
        public uint current_outfit_index { get; set; }

        [ProtoMember(91, DataFormat = DataFormat.FixedSize)]
        public ulong voice_effect { get; set; }

        [ProtoMember(148)]
        public float skin_tone_val_shift { get; set; }

        [ProtoMember(173)]
        public PeltLayerDataList? pelt_layers { get; set; }
    }

    [ProtoContract]
    public sealed class ZoneData : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1, DataFormat = DataFormat.FixedSize)]
        public ulong zone_id { get; set; }

        [ProtoMember(2)]
        public string name { get; set; } = string.Empty;

        [ProtoMember(6, DataFormat = DataFormat.FixedSize)]
        public ulong household_id { get; set; }
    }

    [ProtoContract]
    public sealed class OutfitList : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1)]
        public List<OutfitData> outfits { get; } = new();
    }

    [ProtoContract]
    public sealed class OutfitData : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1, DataFormat = DataFormat.FixedSize)]
        public ulong outfit_id { get; set; }

        [ProtoMember(2)]
        public uint category { get; set; }

        [ProtoMember(5)]
        public global::EA.Sims4.IdList? parts { get; set; }

        [ProtoMember(6, DataFormat = DataFormat.FixedSize)]
        public ulong created { get; set; }

        [ProtoMember(7)]
        public BodyTypesList? body_types_list { get; set; }

        [ProtoMember(9)]
        public bool match_hair_style { get; set; }

        [ProtoMember(10, DataFormat = DataFormat.FixedSize)]
        public ulong outfit_flags { get; set; }

        [ProtoMember(12)]
        public ColorShiftList? part_shifts { get; set; }
    }

    [ProtoContract]
    public sealed class BodyTypesList : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1)]
        public uint[] body_types { get; set; } = Array.Empty<uint>();
    }

    [ProtoContract]
    public sealed class ColorShiftList : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1)]
        public ulong[] color_shift { get; set; } = Array.Empty<ulong>();
    }

    [ProtoContract]
    public sealed class GeneticData : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1)]
        public byte[] sculpts_and_mods_attr { get; set; } = Array.Empty<byte>();

        [ProtoMember(2)]
        public string physique { get; set; } = string.Empty;

        [ProtoMember(5)]
        public PartDataList? parts_list { get; set; }

        [ProtoMember(6)]
        public PartDataList? growth_parts_list { get; set; }
    }

    [ProtoContract]
    public sealed class PartDataList : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1)]
        public List<PartData> parts { get; } = new();
    }

    [ProtoContract]
    public sealed class PartData : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1, DataFormat = DataFormat.FixedSize)]
        public ulong id { get; set; }

        [ProtoMember(2)]
        public uint body_type { get; set; }

        [ProtoMember(3, DataFormat = DataFormat.FixedSize)]
        public ulong color_shift { get; set; }
    }

    [ProtoContract]
    public sealed class PeltLayerDataList : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1)]
        public List<PeltLayerData> layers { get; } = new();
    }

    [ProtoContract]
    public sealed class PeltLayerData : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1, DataFormat = DataFormat.FixedSize)]
        public ulong layer_id { get; set; }

        [ProtoMember(2)]
        public uint color { get; set; }
    }

    [ProtoContract]
    public sealed class PersistableSimInfoAttributes : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(17)]
        public TraitTrackerAttributes? trait_tracker { get; set; }
    }

    [ProtoContract]
    public sealed class TraitTrackerAttributes : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1, DataFormat = DataFormat.FixedSize)]
        public ulong[] trait_ids { get; set; } = Array.Empty<ulong>();
    }

    [ProtoContract]
    public sealed class BlobSimFacialCustomizationData : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1, DataFormat = DataFormat.FixedSize)]
        public ulong[] sculpts { get; set; } = Array.Empty<ulong>();

        [ProtoMember(2)]
        public List<Modifier> face_modifiers { get; } = new();

        [ProtoMember(3)]
        public List<Modifier> body_modifiers { get; } = new();
    }

    [ProtoContract]
    public sealed class Modifier : IExtensible
    {
        private IExtension? _extensionData;

        IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
            Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

        [ProtoMember(1, DataFormat = DataFormat.FixedSize)]
        public ulong key { get; set; }

        [ProtoMember(2)]
        public float amount { get; set; }
    }
}
