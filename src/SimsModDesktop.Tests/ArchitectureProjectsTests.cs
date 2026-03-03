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
            typeof(SimsModDesktop.Presentation.Workspaces.MainShellViewModel).Assembly,
            "SimsModDesktop.Infrastructure");
    }

    [Fact]
    public void App_Contains_No_UseCase_Or_Repository_Types()
    {
        var appAssembly = Assembly.Load("SimsModDesktop.App");
        var invalidTypes = appAssembly
            .GetTypes()
            .Where(type =>
                (type.Namespace?.Contains(".UseCases", StringComparison.Ordinal) ?? false) ||
                type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.True(
            invalidTypes.Length == 0,
            "App assembly should not define use case or repository types:" + Environment.NewLine + string.Join(Environment.NewLine, invalidTypes));
    }

    private static void AssertNoAssemblyReference(Assembly assembly, string forbiddenAssemblyName)
    {
        var references = assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(
            references,
            reference => string.Equals(reference.Name, forbiddenAssemblyName, StringComparison.Ordinal));
    }
}
