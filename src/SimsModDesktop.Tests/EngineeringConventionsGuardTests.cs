namespace SimsModDesktop.Tests;

public sealed class EngineeringConventionsGuardTests
{
    [Fact]
    public void PresentationDiagnostics_Files_Use_PresentationDiagnostics_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertNamespaceDeclaration(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Presentation", "Diagnostics"),
            "namespace SimsModDesktop.Presentation.Diagnostics;",
            "Every file in Presentation/Diagnostics must use the SimsModDesktop.Presentation.Diagnostics namespace:");
    }

    [Fact]
    public void DesktopHostDiagnostics_Files_Use_DesktopHostDiagnostics_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertNamespaceDeclaration(
            repoRoot,
            Path.Combine("src", "SimsModDesktop", "Diagnostics"),
            "namespace SimsModDesktop.Diagnostics;",
            "Every file in DesktopHost/Diagnostics must use the SimsModDesktop.Diagnostics namespace:");
    }

    [Fact]
    public void PresentationServiceRegistration_Files_Use_PresentationServiceRegistration_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertNamespaceDeclaration(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Presentation", "ServiceRegistration"),
            "namespace SimsModDesktop.Presentation.ServiceRegistration;",
            "Every file in Presentation/ServiceRegistration must use the SimsModDesktop.Presentation.ServiceRegistration namespace:");
    }

    [Fact]
    public void PresentationViewModels_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Presentation", "ViewModels"),
            "SimsModDesktop.Presentation.ViewModels",
            "Every file in Presentation/ViewModels must use the namespace derived from its folder:");
    }

    [Fact]
    public void PresentationServices_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Presentation", "Services"),
            "SimsModDesktop.Presentation.Services",
            "Every file in Presentation/Services must use the namespace derived from its folder:");
    }

    [Fact]
    public void PresentationDialogs_Files_Use_PresentationDialogs_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Presentation", "Dialogs"),
            "SimsModDesktop.Presentation.Dialogs",
            "Every file in Presentation/Dialogs must use the namespace derived from its folder:");
    }

    [Fact]
    public void DesktopViews_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop", "Views"),
            "SimsModDesktop.Views",
            "Every file in DesktopHost/Views must use the namespace derived from its folder:");
    }

    [Fact]
    public void ApplicationExecution_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Application", "Execution"),
            "SimsModDesktop.Application.Execution",
            "Every file in Application/Execution must use the namespace derived from its folder:");
    }

    [Fact]
    public void ApplicationServiceRegistration_Files_Use_ApplicationServiceRegistration_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Application", "ServiceRegistration"),
            "SimsModDesktop.Application.ServiceRegistration",
            "Every file in Application/ServiceRegistration must use the namespace derived from its folder:");
    }

    [Fact]
    public void InfrastructureServices_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Infrastructure", "Services"),
            "SimsModDesktop.Infrastructure.Services",
            "Every file in Infrastructure/Services must use the namespace derived from its folder:");
    }

    [Fact]
    public void InfrastructureConfiguration_Files_Use_InfrastructureConfiguration_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Infrastructure", "Configuration"),
            "SimsModDesktop.Infrastructure.Configuration",
            "Every file in Infrastructure/Configuration must use the namespace derived from its folder:");
    }

    [Fact]
    public void InfrastructureServiceRegistration_Files_Use_InfrastructureServiceRegistration_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Infrastructure", "ServiceRegistration"),
            "SimsModDesktop.Infrastructure.ServiceRegistration",
            "Every file in Infrastructure/ServiceRegistration must use the namespace derived from its folder:");
    }

    [Fact]
    public void ApplicationRequests_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Application", "Requests"),
            "SimsModDesktop.Application.Requests",
            "Every file in Application/Requests must use the namespace derived from its folder:");
    }

    [Fact]
    public void ApplicationResults_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Application", "Results"),
            "SimsModDesktop.Application.Results",
            "Every file in Application/Results must use the namespace derived from its folder:");
    }

    [Fact]
    public void ApplicationValidation_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Application", "Validation"),
            "SimsModDesktop.Application.Validation",
            "Every file in Application/Validation must use the namespace derived from its folder:");
    }

    [Fact]
    public void ApplicationTrayPreview_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Application", "TrayPreview"),
            "SimsModDesktop.Application.TrayPreview",
            "Every file in Application/TrayPreview must use the namespace derived from its folder:");
    }

    [Fact]
    public void ApplicationCaching_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Application", "Caching"),
            "SimsModDesktop.Application.Caching",
            "Every file in Application/Caching must use the namespace derived from its folder:");
    }

    [Fact]
    public void InfrastructureTray_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Infrastructure", "Tray"),
            "SimsModDesktop.Infrastructure.Tray",
            "Every file in Infrastructure/Tray must use the namespace derived from its folder:");
    }

    [Fact]
    public void InfrastructurePersistence_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Infrastructure", "Persistence"),
            "SimsModDesktop.Infrastructure.Persistence",
            "Every file in Infrastructure/Persistence must use the namespace derived from its folder:");
    }

    [Fact]
    public void InfrastructureSettings_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Infrastructure", "Settings"),
            "SimsModDesktop.Infrastructure.Settings",
            "Every file in Infrastructure/Settings must use the namespace derived from its folder:");
    }

    [Fact]
    public void InfrastructureMods_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Infrastructure", "Mods"),
            "SimsModDesktop.Infrastructure.Mods",
            "Every file in Infrastructure/Mods must use the namespace derived from its folder:");
    }

    [Fact]
    public void PresentationShell_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Presentation", "Shell"),
            "SimsModDesktop.Presentation.Shell",
            "Every file in Presentation/Shell must use the namespace derived from its folder:");
    }

    [Fact]
    public void PresentationPreview_Files_Use_DirectoryDerived_Namespace()
    {
        var repoRoot = FindRepoRoot();
        AssertRecursiveNamespaceDeclarations(
            repoRoot,
            Path.Combine("src", "SimsModDesktop.Presentation", "Preview"),
            "SimsModDesktop.Presentation.Preview",
            "Every file in Presentation/Preview must use the namespace derived from its folder:");
    }

    [Fact]
    public void AppStartupTelemetry_Exists_Only_In_DesktopHost_Diagnostics()
    {
        var repoRoot = FindRepoRoot();
        var shellTelemetryPath = Path.Combine(repoRoot, "src", "SimsModDesktop", "Diagnostics", "AppStartupTelemetry.cs");
        var presentationTelemetryPath = Path.Combine(repoRoot, "src", "SimsModDesktop.Presentation", "Diagnostics", "AppStartupTelemetry.cs");

        Assert.True(File.Exists(shellTelemetryPath), $"Desktop host startup telemetry file is missing: {shellTelemetryPath}");
        Assert.False(
            File.Exists(presentationTelemetryPath),
            $"Startup telemetry must not live in Presentation diagnostics: {presentationTelemetryPath}");
    }

    [Fact]
    public void DesktopHost_DoesNot_Contain_Presentation_Diagnostics_Namespace_Types()
    {
        var shellAssembly = typeof(SimsModDesktop.Program).Assembly;
        var invalidTypes = shellAssembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("SimsModDesktop.Presentation.Diagnostics", StringComparison.Ordinal) == true)
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.True(
            invalidTypes.Length == 0,
            "Desktop shell assembly should not define Presentation diagnostics types:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, invalidTypes));
    }

    [Fact]
    public void PresentationDiagnostics_Do_Not_Expose_Public_Types()
    {
        var assembly = typeof(SimsModDesktop.Presentation.ViewModels.MainWindowViewModel).Assembly;
        var invalidTypes = assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("SimsModDesktop.Presentation.Diagnostics", StringComparison.Ordinal) == true)
            .Where(type => type.IsPublic)
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.True(
            invalidTypes.Length == 0,
            "Presentation diagnostics should not expose public types:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, invalidTypes));
    }

    [Fact]
    public void DesktopHostDiagnostics_Do_Not_Expose_Public_Types()
    {
        var assembly = typeof(SimsModDesktop.Program).Assembly;
        var invalidTypes = assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("SimsModDesktop.Diagnostics", StringComparison.Ordinal) == true)
            .Where(type => type.IsPublic)
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.True(
            invalidTypes.Length == 0,
            "Desktop host diagnostics should not expose public types:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, invalidTypes));
    }

    [Fact]
    public void InfrastructureMods_Do_Not_Expose_Public_Inventory_Implementation()
    {
        var assembly = typeof(SimsModDesktop.Infrastructure.ServiceRegistration.InfrastructureServiceRegistration).Assembly;
        var inventoryType = assembly.GetType("SimsModDesktop.Infrastructure.Mods.SqliteModPackageInventoryService");

        Assert.NotNull(inventoryType);
        Assert.False(
            inventoryType!.IsPublic,
            "SqliteModPackageInventoryService should remain internal and be consumed via IModPackageInventoryService.");
    }

    private static void AssertNamespaceDeclaration(
        string repoRoot,
        string relativeDirectory,
        string requiredDeclaration,
        string messagePrefix)
    {
        var directoryPath = Path.Combine(repoRoot, relativeDirectory);

        Assert.True(Directory.Exists(directoryPath), $"Expected directory is missing: {directoryPath}");

        var violations = Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !File.ReadAllText(path).Contains(requiredDeclaration, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            messagePrefix +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static void AssertRecursiveNamespaceDeclarations(
        string repoRoot,
        string relativeDirectory,
        string baseNamespace,
        string messagePrefix)
    {
        var directoryPath = Path.Combine(repoRoot, relativeDirectory);

        Assert.True(Directory.Exists(directoryPath), $"Expected directory is missing: {directoryPath}");

        var violations = Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !File.ReadAllText(path).Contains($"namespace {BuildExpectedNamespace(directoryPath, path, baseNamespace)};", StringComparison.Ordinal))
            .Select(path => $"{Path.GetRelativePath(repoRoot, path)} -> expected namespace {BuildExpectedNamespace(directoryPath, path, baseNamespace)}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            messagePrefix +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static string BuildExpectedNamespace(string rootDirectory, string filePath, string baseNamespace)
    {
        var fileDirectory = Path.GetDirectoryName(filePath) ?? rootDirectory;
        var relativeDirectory = Path.GetRelativePath(rootDirectory, fileDirectory);
        if (string.IsNullOrWhiteSpace(relativeDirectory) ||
            string.Equals(relativeDirectory, ".", StringComparison.Ordinal))
        {
            return baseNamespace;
        }

        var segments = relativeDirectory
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !string.Equals(segment, ".", StringComparison.Ordinal));

        return baseNamespace + "." + string.Join(".", segments);
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "SimsDesktopTools.sln")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
