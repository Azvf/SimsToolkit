using System.Reflection;

namespace SimsModDesktop.Tests;

public sealed class ArchitectureProjectsTests
{
    [Fact]
    public void Application_DoesNotReference_Infrastructure()
    {
        AssertNoAssemblyReference(
            typeof(SimsModDesktop.Application.UseCases.IUseCase<,>).Assembly,
            "SimsModDesktop.Infrastructure");
    }

    [Fact]
    public void Presentation_DoesNotReference_Infrastructure()
    {
        AssertNoAssemblyReference(
            typeof(SimsModDesktop.Presentation.ViewModels.MainWindowViewModel).Assembly,
            "SimsModDesktop.Infrastructure");
    }

    [Fact]
    public void Presentation_Contains_RuntimeViewModelTypes()
    {
        var presentationAssembly = typeof(SimsModDesktop.Presentation.ViewModels.MainWindowViewModel).Assembly;

        Assert.Equal("SimsModDesktop.Presentation", presentationAssembly.GetName().Name);
        Assert.Contains(
            presentationAssembly.GetTypes(),
            type => string.Equals(type.FullName, "SimsModDesktop.Presentation.ViewModels.MainWindowViewModel", StringComparison.Ordinal));
        Assert.Contains(
            presentationAssembly.GetTypes(),
            type => string.Equals(type.FullName, "SimsModDesktop.Presentation.ViewModels.Shell.MainShellViewModel", StringComparison.Ordinal));
    }

    [Fact]
    public void DesktopShell_Contains_No_UseCase_Or_Repository_Types()
    {
        var shellAssembly = Assembly.Load("SimsModDesktop");
        var invalidTypes = shellAssembly
            .GetTypes()
            .Where(type =>
                (type.Namespace?.Contains(".UseCases", StringComparison.Ordinal) ?? false) ||
                type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.True(
            invalidTypes.Length == 0,
            "Desktop shell assembly should not define use case or repository types:" + Environment.NewLine + string.Join(Environment.NewLine, invalidTypes));
    }

    [Fact]
    public void DesktopShell_Contains_No_ViewModel_Types()
    {
        var shellAssembly = Assembly.Load("SimsModDesktop");
        var invalidTypes = shellAssembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("SimsModDesktop.Presentation.ViewModels", StringComparison.Ordinal) == true)
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.True(
            invalidTypes.Length == 0,
            "Desktop shell assembly should not define view model types:" + Environment.NewLine + string.Join(Environment.NewLine, invalidTypes));
    }

    [Fact]
    public void Infrastructure_DoesNotContain_NoOpUseCase_Placeholders()
    {
        var infrastructureAssembly = typeof(SimsModDesktop.Infrastructure.ServiceRegistration.InfrastructureServiceRegistration).Assembly;

        var invalidTypes = infrastructureAssembly
            .GetTypes()
            .Where(type => type.Name.StartsWith("NoOp", StringComparison.Ordinal) &&
                           type.Name.EndsWith("UseCase", StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.True(
            invalidTypes.Length == 0,
            "Infrastructure should not contain NoOp use case placeholders:" + Environment.NewLine + string.Join(Environment.NewLine, invalidTypes));
    }

    private static void AssertNoAssemblyReference(Assembly assembly, string forbiddenAssemblyName)
    {
        var references = assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(
            references,
            reference => string.Equals(reference.Name, forbiddenAssemblyName, StringComparison.Ordinal));
    }
}
