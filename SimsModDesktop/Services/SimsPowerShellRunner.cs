using System.Diagnostics;
using SimsModDesktop.Application.Cli;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class SimsPowerShellRunner : ISimsPowerShellRunner
{
    private const string ProgressPrefix = "##SIMS_PROGRESS##|";

    public async Task<SimsExecutionResult> RunAsync(
        SimsProcessCommand command,
        Action<string> onOutput,
        Action<SimsProgressUpdate>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(onOutput);

        var executable = ResolvePowerShellExecutable();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                return;
            }

            if (TryParseProgressLine(eventArgs.Data, out var progress))
            {
                onProgress?.Invoke(progress);
            }
            else
            {
                onOutput(eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                onOutput("[stderr] " + eventArgs.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start PowerShell process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore cancellation race and process exit errors.
            }
        });

        await process.WaitForExitAsync(cancellationToken);

        return new SimsExecutionResult
        {
            ExitCode = process.ExitCode,
            Executable = executable,
            Arguments = startInfo.ArgumentList.ToList()
        };
    }

    internal static bool TryParseProgressLine(string line, out SimsProgressUpdate progress)
    {
        progress = null!;

        if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = line.Substring(ProgressPrefix.Length);
        var parts = payload.Split('|', 5, StringSplitOptions.None);
        if (parts.Length < 4)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var current))
        {
            return false;
        }

        if (!int.TryParse(parts[2], out var total))
        {
            return false;
        }

        if (!int.TryParse(parts[3], out var percent))
        {
            return false;
        }

        progress = new SimsProgressUpdate
        {
            Stage = parts[0],
            Current = current,
            Total = total,
            Percent = percent,
            Detail = parts.Length >= 5 ? parts[4] : string.Empty
        };
        return true;
    }

    private static string ResolvePowerShellExecutable()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "pwsh.exe", "powershell.exe" }
            : new[] { "pwsh" };

        foreach (var candidate in candidates)
        {
            if (IsExecutableOnPath(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            OperatingSystem.IsWindows()
                ? "PowerShell executable not found. Install PowerShell 7 (pwsh) or enable powershell.exe."
                : "PowerShell executable not found. Install PowerShell 7 (pwsh) and ensure it is on PATH.");
    }

    private static bool IsExecutableOnPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        var directories = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return true;
            }
        }

        return false;
    }
}
