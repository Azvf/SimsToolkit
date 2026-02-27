using SimsModDesktop.Application.Cli;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public interface ISimsPowerShellRunner
{
    Task<SimsExecutionResult> RunAsync(
        SimsProcessCommand command,
        Action<string> onOutput,
        Action<SimsProgressUpdate>? onProgress = null,
        CancellationToken cancellationToken = default);
}
