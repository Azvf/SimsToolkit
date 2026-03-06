using System.Text;

namespace SimsModDesktop.PackageCore;

public static class Ts4FnvHash
{
    private const ulong FnvPrime64 = 1099511628211;
    private const ulong FnvOffset64 = 14695981039346656037;

    public static ulong Fnv64(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var hash = FnvOffset64;
        var normalized = value.ToLowerInvariant();
        var bytes = Encoding.ASCII.GetBytes(normalized);
        foreach (var b in bytes)
        {
            unchecked
            {
                hash *= FnvPrime64;
            }

            hash ^= b;
        }

        return hash;
    }
}
