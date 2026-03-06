using SimsModDesktop.Application.Caching;
using SimsModDesktop.Presentation.ViewModels;

namespace SimsModDesktop.Presentation.Warmup;

internal static class WarmupProgressPublisher
{
    public static void ReportProgress(MainWindowCacheWarmupHost host, CacheWarmupProgress progress)
    {
        try
        {
            host.ReportProgress(progress);
        }
        catch
        {
        }
    }

    public static void AppendLog(MainWindowCacheWarmupHost host, string message)
    {
        try
        {
            host.AppendLog(message);
        }
        catch
        {
        }
    }
}
