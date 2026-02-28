using System.Diagnostics;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class GameLaunchService : IGameLaunchService
{
    public LaunchGameResult Launch(LaunchGameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ExecutablePath))
        {
            return new LaunchGameResult
            {
                Success = false,
                Message = "Game executable path is required."
            };
        }

        var executablePath = request.ExecutablePath.Trim();
        if (!File.Exists(executablePath))
        {
            return new LaunchGameResult
            {
                Success = false,
                Message = $"Game executable not found: {executablePath}"
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory.Trim();
        }

        foreach (var argument in request.Arguments)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        try
        {
            var process = Process.Start(startInfo);
            if (process is null)
            {
                return new LaunchGameResult
                {
                    Success = false,
                    Message = "Failed to start game process."
                };
            }

            return new LaunchGameResult
            {
                Success = true,
                ProcessId = process.Id,
                Message = "Game launched."
            };
        }
        catch (Exception ex)
        {
            return new LaunchGameResult
            {
                Success = false,
                Message = $"Failed to launch game: {ex.Message}"
            };
        }
    }
}
