using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.ServiceRegistration;
using SimsModDesktop.Infrastructure.ServiceRegistration;
using SimsModDesktop.Presentation.ServiceRegistration;

namespace SimsModDesktop.App;

internal static class Program
{
    private static void Main()
    {
        _ = new ServiceCollection()
            .AddSimsModDesktopApplication()
            .AddSimsModDesktopInfrastructure()
            .AddSimsModDesktopPresentation()
            .AddSimsModDesktopApp()
            .BuildServiceProvider(validateScopes: true);
    }
}
