using Microsoft.Extensions.DependencyInjection;

namespace SimsModDesktop.Presentation.ServiceRegistration;

public static class PresentationServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
