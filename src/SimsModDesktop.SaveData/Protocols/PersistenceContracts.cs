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
}
