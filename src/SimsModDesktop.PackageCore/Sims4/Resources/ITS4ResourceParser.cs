namespace SimsModDesktop.PackageCore;

public interface ITS4ResourceParser<T>
{
    bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out T result, out string? error);
}
