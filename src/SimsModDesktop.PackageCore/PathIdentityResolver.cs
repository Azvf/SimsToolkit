using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SimsModDesktop.PackageCore;

public interface IPathIdentityResolver
{
    ResolvedPathInfo ResolveDirectory(string path);

    ResolvedPathInfo ResolveFile(string path);

    bool EqualsDirectory(string? left, string? right);
}

public sealed record ResolvedPathInfo
{
    public required string InputPath { get; init; }
    public required string FullPath { get; init; }
    public required string CanonicalPath { get; init; }
    public bool Exists { get; init; }
    public bool IsReparsePoint { get; init; }
    public string? LinkTarget { get; init; }
}

public sealed class SystemPathIdentityResolver : IPathIdentityResolver
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public ResolvedPathInfo ResolveDirectory(string path)
    {
        return ResolveCore(path, asDirectory: true);
    }

    public ResolvedPathInfo ResolveFile(string path)
    {
        return ResolveCore(path, asDirectory: false);
    }

    public bool EqualsDirectory(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftResolved = ResolveDirectory(left);
        var rightResolved = ResolveDirectory(right);
        return string.Equals(leftResolved.CanonicalPath, rightResolved.CanonicalPath, PathComparison);
    }

    private static ResolvedPathInfo ResolveCore(string path, bool asDirectory)
    {
        var input = path ?? string.Empty;
        var trimmed = input.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return new ResolvedPathInfo
            {
                InputPath = input,
                FullPath = string.Empty,
                CanonicalPath = string.Empty,
                Exists = false,
                IsReparsePoint = false,
                LinkTarget = null
            };
        }

        var fullPath = NormalizePath(TryGetFullPath(trimmed));
        var exists = asDirectory
            ? Directory.Exists(fullPath)
            : File.Exists(fullPath);
        if (!exists)
        {
            return new ResolvedPathInfo
            {
                InputPath = input,
                FullPath = fullPath,
                CanonicalPath = fullPath,
                Exists = false,
                IsReparsePoint = false,
                LinkTarget = null
            };
        }

        var isReparsePoint = false;
        var canonicalPath = fullPath;
        string? linkTarget = null;
        try
        {
            FileSystemInfo info = asDirectory
                ? new DirectoryInfo(fullPath)
                : new FileInfo(fullPath);
            isReparsePoint = info.Exists &&
                             (info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            if (isReparsePoint)
            {
                var resolvedTarget = info.ResolveLinkTarget(returnFinalTarget: true);
                if (resolvedTarget is not null)
                {
                    linkTarget = NormalizePath(TryGetFullPath(resolvedTarget.FullName));
                }
            }
        }
        catch
        {
            // Best-effort metadata extraction only.
        }

        var finalPath = TryGetFinalPathByHandle(fullPath, asDirectory);
        if (!string.IsNullOrWhiteSpace(finalPath))
        {
            canonicalPath = NormalizePath(finalPath);
        }
        else if (!string.IsNullOrWhiteSpace(linkTarget))
        {
            canonicalPath = linkTarget;
        }

        return new ResolvedPathInfo
        {
            InputPath = input,
            FullPath = fullPath,
            CanonicalPath = canonicalPath,
            Exists = true,
            IsReparsePoint = isReparsePoint,
            LinkTarget = linkTarget
        };
    }

    private static string TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Trim();
        if (OperatingSystem.IsWindows())
        {
            if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                normalized = @"\\" + normalized[8..];
            }
            else if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[4..];
            }
        }

        return Path.TrimEndingDirectorySeparator(normalized);
    }

    private static string? TryGetFinalPathByHandle(string path, bool asDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var flags = asDirectory ? FileFlags.BackupSemantics : 0u;
        var handle = NativeMethods.CreateFile(
            path,
            desiredAccess: 0,
            shareMode: (uint)(FileShare.ReadWrite | FileShare.Delete),
            IntPtr.Zero,
            creationDisposition: 3u,
            flagsAndAttributes: flags,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        using (handle)
        {
            var builder = new StringBuilder(512);
            var chars = NativeMethods.GetFinalPathNameByHandle(
                handle,
                builder,
                (uint)builder.Capacity,
                0);
            if (chars == 0)
            {
                return null;
            }

            if (chars > builder.Capacity)
            {
                builder.EnsureCapacity((int)chars);
                chars = NativeMethods.GetFinalPathNameByHandle(
                    handle,
                    builder,
                    (uint)builder.Capacity,
                    0);
                if (chars == 0)
                {
                    return null;
                }
            }

            return builder.ToString();
        }
    }

    private static class FileFlags
    {
        public const uint BackupSemantics = 0x02000000;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFinalPathNameByHandle(
            SafeFileHandle file,
            StringBuilder filePath,
            uint filePathCapacity,
            uint flags);
    }
}
