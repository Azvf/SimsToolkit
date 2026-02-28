using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class GameServicesTests
{
    [Fact]
    public void GameLaunchService_InvalidPath_ReturnsFailure()
    {
        var service = new GameLaunchService();

        var result = service.Launch(new LaunchGameRequest
        {
            ExecutablePath = @"C:\not-found\TS4_x64.exe"
        });

        Assert.False(result.Success);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ts4PathDiscovery_ReturnsCandidateList()
    {
        var service = new TS4PathDiscoveryService();

        var result = service.Discover();

        Assert.NotNull(result.GameExecutableCandidates);
        Assert.NotEmpty(result.GameExecutableCandidates);
    }
}
