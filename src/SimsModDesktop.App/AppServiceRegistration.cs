using Microsoft.Extensions.DependencyInjection;

namespace SimsModDesktop.App;

public static class AppServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopApp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
