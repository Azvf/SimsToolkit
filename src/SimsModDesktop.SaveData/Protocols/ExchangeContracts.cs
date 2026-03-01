using ProtoBuf;

namespace EA.Sims4.Exchange;

[ProtoContract]
public enum ExchangeItemTypes
{
    [ProtoEnum]
    EXCHANGE_INVALIDTYPE = 0,
    [ProtoEnum]
    EXCHANGE_HOUSEHOLD = 1,
    [ProtoEnum]
    EXCHANGE_BLUEPRINT = 2,
    [ProtoEnum]
    EXCHANGE_ROOM = 3,
    [ProtoEnum]
    EXCHANGE_ALLTYPES = 4,
    [ProtoEnum]
    EXCHANGE_PART = 5
}

[ProtoContract]
public sealed class TrayMetadata : IExtensible
{
    private IExtension? _extensionData;

    IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
        Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

    [ProtoMember(1)]
    public ulong id { get; set; }

    [ProtoMember(2)]
    public ExchangeItemTypes type { get; set; } = ExchangeItemTypes.EXCHANGE_INVALIDTYPE;

    [ProtoMember(4)]
    public string name { get; set; } = string.Empty;

    [ProtoMember(5)]
    public string description { get; set; } = string.Empty;

    [ProtoMember(6)]
    public ulong creator_id { get; set; }

    [ProtoMember(7)]
    public string creator_name { get; set; } = string.Empty;

    [ProtoMember(10)]
    public SpecificData? metadata { get; set; }

    [ProtoMember(11)]
    public ulong item_timestamp { get; set; }
}

[ProtoContract]
public sealed class TrayHouseholdMetadata : IExtensible
{
    private IExtension? _extensionData;

    IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
        Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

    [ProtoMember(1)]
    public uint family_size { get; set; }

    [ProtoMember(2)]
    public List<TraySimMetadata> sim_data { get; } = new();

    [ProtoMember(3)]
    public uint pending_babies { get; set; }
}

[ProtoContract]
public sealed class TraySimMetadata : IExtensible
{
    private IExtension? _extensionData;

    IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
        Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

    [ProtoMember(3)]
    public string first_name { get; set; } = string.Empty;

    [ProtoMember(4)]
    public string last_name { get; set; } = string.Empty;

    [ProtoMember(5)]
    public ulong id { get; set; }

    [ProtoMember(6)]
    public uint gender { get; set; }

    [ProtoMember(9)]
    public uint age { get; set; }

    [ProtoMember(12)]
    public uint species { get; set; }

    [ProtoMember(14)]
    public uint occult_types { get; set; }
}

[ProtoContract]
public sealed class SpecificData : IExtensible
{
    private IExtension? _extensionData;

    IExtension IExtensible.GetExtensionObject(bool createIfMissing) =>
        Extensible.GetExtensionObject(ref _extensionData, createIfMissing);

    [ProtoMember(2)]
    public TrayHouseholdMetadata? hh_metadata { get; set; }

    [ProtoMember(3)]
    public bool is_hidden { get; set; }
}
