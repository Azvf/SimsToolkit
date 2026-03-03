using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.ServiceRegistration;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Application.UseCases;
using SimsModDesktop.Infrastructure.ServiceRegistration;
using SimsModDesktop.Presentation.ServiceRegistration;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.Presentation.ViewModels.Shell;

namespace SimsModDesktop.Tests;

public sealed class LayeredServiceRegistrationTests
{
    [Fact]
    public void ApplicationRegistration_RegistersCoreApplicationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSimsModDesktopApplication();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutionCoordinator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IToolkitActionPlanner));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IFileTransformationEngine));
    }

    [Fact]
    public void PresentationRegistration_RegistersShellComposition()
    {
        var services = new ServiceCollection();
        services.AddSimsModDesktopPresentation();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(INavigationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(MainShellViewModel));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(MainWindowViewModel));
    }

    [Fact]
    public void DesktopShellRegistration_ComposesLayeredRegistrations()
    {
        var services = new ServiceCollection();
        var registrationType = Type.GetType("SimsModDesktop.Composition.ServiceCollectionExtensions, SimsModDesktop");
        Assert.NotNull(registrationType);

        var registerMethod = registrationType!.GetMethod("AddSimsDesktopShell");
        Assert.NotNull(registerMethod);

        _ = registerMethod!.Invoke(null, [services]);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<MainShellViewModel>());
        Assert.NotNull(provider.GetRequiredService<MainWindowViewModel>());
        Assert.NotNull(provider.GetRequiredService<ITextureCompressionService>());
        Assert.NotNull(provider.GetRequiredService<IConfigurationProvider>());
        Assert.NotNull(provider.GetRequiredService<IOrganizeModsUseCase>());
    }
}
