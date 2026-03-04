using Avalonia;
using System;
using SimsModDesktop.Diagnostics;

namespace SimsModDesktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppStartupTelemetry.ResetForMainEntry();
        AppStartupTelemetry.RecordMilestone("process.main.enter");
        var builder = BuildAvaloniaApp();
        AppStartupTelemetry.RecordMilestone("avalonia.app.built");
        builder.StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
