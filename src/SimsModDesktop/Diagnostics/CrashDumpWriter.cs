using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SimsModDesktop.Diagnostics;

internal static class CrashDumpWriter
{
    private const int MaxReasonTokenLength = 48;
    private const MiniDumpType DefaultDumpType =
        MiniDumpType.MiniDumpWithHandleData |
        MiniDumpType.MiniDumpWithUnloadedModules |
        MiniDumpType.MiniDumpWithProcessThreadData |
        MiniDumpType.MiniDumpWithThreadInfo;

    public static bool TryWrite(string reason, Exception? exception, out string? dumpPath, out string? error)
    {
        dumpPath = null;
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var normalizedReason = NormalizeReason(reason);
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache",
                "Logs",
                "Dumps");
            Directory.CreateDirectory(directory);

            dumpPath = Path.Combine(
                directory,
                $"simsmoddesktop_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Environment.ProcessId}_{normalizedReason}.dmp");

            using var process = Process.GetCurrentProcess();
            using var stream = new FileStream(dumpPath, FileMode.Create, FileAccess.Write, FileShare.Read);

            var processHandle = process.Handle;
            var processId = process.Id;
            var fileHandle = stream.SafeFileHandle.DangerousGetHandle();
            var exceptionPointers = GetExceptionPointersSafe(exception);

            var success = exceptionPointers != IntPtr.Zero
                ? WriteWithExceptionPointers(processHandle, processId, fileHandle, exceptionPointers)
                : WriteWithoutExceptionPointers(processHandle, processId, fileHandle);

            if (!success)
            {
                var win32Error = Marshal.GetLastWin32Error();
                error = $"MiniDumpWriteDump failed with Win32 error {win32Error}.";
                TryDeleteIncompleteDump(dumpPath);
                dumpPath = null;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            if (!string.IsNullOrWhiteSpace(dumpPath))
            {
                TryDeleteIncompleteDump(dumpPath);
            }

            dumpPath = null;
            return false;
        }
    }

    private static IntPtr GetExceptionPointersSafe(Exception? exception)
    {
        if (exception is null)
        {
            return IntPtr.Zero;
        }

        try
        {
            return Marshal.GetExceptionPointers();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static bool WriteWithExceptionPointers(IntPtr processHandle, int processId, IntPtr fileHandle, IntPtr exceptionPointers)
    {
        var info = new MinidumpExceptionInformation
        {
            ThreadId = GetCurrentThreadId(),
            ExceptionPointers = exceptionPointers,
            ClientPointers = false
        };

        return MiniDumpWriteDump(
            processHandle,
            processId,
            fileHandle,
            DefaultDumpType,
            ref info,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private static bool WriteWithoutExceptionPointers(IntPtr processHandle, int processId, IntPtr fileHandle)
    {
        return MiniDumpWriteDump(
            processHandle,
            processId,
            fileHandle,
            DefaultDumpType,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private static void TryDeleteIncompleteDump(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string NormalizeReason(string reason)
    {
        var token = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim().ToLowerInvariant();
        Span<char> buffer = stackalloc char[token.Length];
        var written = 0;
        foreach (var ch in token)
        {
            if (written >= buffer.Length)
            {
                break;
            }

            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                buffer[written++] = ch;
                continue;
            }

            if (ch == '_' || ch == '-')
            {
                buffer[written++] = ch;
                continue;
            }

            buffer[written++] = '_';
        }

        if (written == 0)
        {
            return "unknown";
        }

        var normalized = new string(buffer[..written]);
        if (normalized.Length <= MaxReasonTokenLength)
        {
            return normalized;
        }

        return normalized[..MaxReasonTokenLength];
    }

    [Flags]
    private enum MiniDumpType : uint
    {
        MiniDumpWithHandleData = 0x00000004,
        MiniDumpWithUnloadedModules = 0x00000020,
        MiniDumpWithProcessThreadData = 0x00000100,
        MiniDumpWithThreadInfo = 0x00001000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinidumpExceptionInformation
    {
        public uint ThreadId;
        public IntPtr ExceptionPointers;

        [MarshalAs(UnmanagedType.Bool)]
        public bool ClientPointers;
    }

    [DllImport("Dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(
        IntPtr processHandle,
        int processId,
        IntPtr fileHandle,
        MiniDumpType dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    [DllImport("Dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(
        IntPtr processHandle,
        int processId,
        IntPtr fileHandle,
        MiniDumpType dumpType,
        ref MinidumpExceptionInformation exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    [DllImport("Kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
