using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public interface IGameLaunchService
{
    LaunchGameResult Launch(LaunchGameRequest request);
}
