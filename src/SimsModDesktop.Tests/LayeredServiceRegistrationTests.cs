using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Cli;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Application.ServiceRegistration;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Application.UseCases;
using SimsModDesktop.Infrastructure.ServiceRegistration;
using SimsModDesktop.Presentation.ServiceRegistration;
using SimsModDesktop.Presentation.Workspaces;

namespace SimsModDesktop.Tests;

public sealed class LayeredServiceRegistrationTests
{
    [Fact]
    public void ApplicationRegistration_RegistersCoreApplicationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSimsModDesktopApplication();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ISimsCliArgumentBuilder>());
        Assert.NotNull(provider.GetRequiredService<IExecutionOutputParserRegistry>());
        Assert.Equal(5, provider.GetServices<Application.Execution.IActionExecutionStrategy>().Count());
    }

    [Fact]
    public void PresentationRegistration_RegistersShellComposition()
    {
        var services = new ServiceCollection();
        services.AddSimsModDesktopPresentation();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<INavigationService>());
        Assert.NotNull(provider.GetRequiredService<MainShellViewModel>());
    }

    [Fact]
    public void AppRegistration_ComposesLayeredRegistrations()
    {
        var services = new ServiceCollection();
        var registrationType = Type.GetType("SimsModDesktop.App.AppServiceRegistration, SimsModDesktop.App");
        Assert.NotNull(registrationType);

        var registerMethod = registrationType!.GetMethod("AddSimsModDesktopApp");
        Assert.NotNull(registerMethod);

        _ = registerMethod!.Invoke(null, [services]);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<MainShellViewModel>());
        Assert.NotNull(provider.GetRequiredService<ITextureCompressionService>());
        Assert.NotNull(provider.GetRequiredService<IConfigurationProvider>());
        Assert.NotNull(provider.GetRequiredService<IOrganizeModsUseCase>());
    }
}
