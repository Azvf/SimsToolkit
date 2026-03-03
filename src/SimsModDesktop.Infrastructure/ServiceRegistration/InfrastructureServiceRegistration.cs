using Microsoft.Extensions.DependencyInjection;

namespace SimsModDesktop.Infrastructure.ServiceRegistration;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
