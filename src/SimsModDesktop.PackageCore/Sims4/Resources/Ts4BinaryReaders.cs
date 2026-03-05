namespace SimsModDesktop.PackageCore;

internal static class Ts4BinaryReaders
{
    public static DbpfResourceKey ReadTgi(BinaryReader reader)
    {
        var type = reader.ReadUInt32();
        var group = reader.ReadUInt32();
        var instance = reader.ReadUInt64();
        return new DbpfResourceKey(type, group, instance);
    }

    public static DbpfResourceKey ReadItg(BinaryReader reader)
    {
        var instance = reader.ReadUInt64();
        var type = reader.ReadUInt32();
        var group = reader.ReadUInt32();
        return new DbpfResourceKey(type, group, instance);
    }

    public static DbpfResourceKey ReadIgt(BinaryReader reader)
    {
        var instance = reader.ReadUInt64();
        var group = reader.ReadUInt32();
        var type = reader.ReadUInt32();
        return new DbpfResourceKey(type, group, instance);
    }
}
