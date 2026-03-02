using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Infrastructure.Services;

namespace SimsModDesktop.Tests;

public sealed class WindowsFileSystemBoundaryIntegrationTests
{
    [Fact]
    public async Task Flatten_WhenTargetLocked_DoesNotThrowAndMarksFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var source = new TempDirectory("boundary-lock-src");
        using var target = new TempDirectory("boundary-lock-dst");

        var sourceFile = Path.Combine(source.Path, "a.package");
        var targetFile = Path.Combine(target.Path, "a.package");
        await File.WriteAllTextAsync(sourceFile, "source");
        await File.WriteAllTextAsync(targetFile, "target");

        using var lockStream = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = await CreateEngine().TransformAsync(
            new TransformationOptions
            {
                SourcePath = source.Path,
                TargetPath = target.Path,
                ConflictStrategy = ConflictResolutionStrategy.Overwrite,
                Recursive = true
            },
            TransformationMode.Flatten);

        Assert.False(result.Success);
        Assert.Equal(1, result.FailedFiles);
    }

    [Fact]
    public async Task Organize_WhenZipCorrupted_RecordsFailureAndWarning()
    {
        using var source = new TempDirectory("boundary-corrupt-zip");
        using var target = new TempDirectory("boundary-corrupt-out");
        var zipPath = Path.Combine(source.Path, "broken.zip");
        await File.WriteAllTextAsync(zipPath, "not-a-zip");

        var result = await CreateEngine().TransformAsync(
            new TransformationOptions
            {
                SourcePath = source.Path,
                TargetPath = target.Path,
                ModeOptions = new ModeSpecificOptions
                {
                    Organize = new OrganizeOptions { KeepZip = true }
                }
            },
            TransformationMode.Organize);

        Assert.False(result.Success);
        Assert.Equal(1, result.FailedFiles);
        Assert.Contains(result.Warnings, warning => warning.Contains("Invalid archive skipped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteFileAsync_WhenReadOnly_CanDeletePermanently()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new CrossPlatformFileOperationService(NullLogger<CrossPlatformFileOperationService>.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"readonly-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "x");
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

        try
        {
            var deleted = await service.DeleteFileAsync(path, permanent: true);
            Assert.True(deleted);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Merge_WhenSourceInaccessible_ContinuesAndAddsWarning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TempDirectory("boundary-inaccessible");
        var inaccessible = Path.Combine(root.Path, "noaccess");
        var accessible = Path.Combine(root.Path, "ok");
        var target = Path.Combine(root.Path, "out");

        Directory.CreateDirectory(inaccessible);
        Directory.CreateDirectory(accessible);
        await File.WriteAllTextAsync(Path.Combine(accessible, "a.package"), "ok");
        await File.WriteAllTextAsync(Path.Combine(inaccessible, "b.package"), "deny");

        var sid = WindowsIdentity.GetCurrent().User;
        if (sid is null)
        {
            return;
        }

        FileSystemAccessRule? denyRule = null;
        var inaccessibleInfo = new DirectoryInfo(inaccessible);
        try
        {
            var security = inaccessibleInfo.GetAccessControl();
            denyRule = new FileSystemAccessRule(
                sid,
                FileSystemRights.ListDirectory | FileSystemRights.ReadData,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Deny);
            security.AddAccessRule(denyRule);
            inaccessibleInfo.SetAccessControl(security);
        }
        catch
        {
            return;
        }

        try
        {
            var result = await CreateEngine().TransformAsync(
                new TransformationOptions
                {
                    SourcePath = accessible,
                    TargetPath = target,
                    KeepSource = true,
                    ModeOptions = new ModeSpecificOptions
                    {
                        Merge = new MergeOptions
                        {
                            SourcePaths = [inaccessible, accessible]
                        }
                    }
                },
                TransformationMode.Merge);

            Assert.True(result.ProcessedFiles >= 1);
            Assert.Contains(result.Warnings, warning => warning.Contains("Access denied", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (denyRule is not null)
            {
                var security = inaccessibleInfo.GetAccessControl();
                security.RemoveAccessRuleSpecific(denyRule);
                inaccessibleInfo.SetAccessControl(security);
            }
        }
    }

    [Fact]
    public async Task Normalize_WithLongPath_DoesNotThrow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TempDirectory("boundary-longpath");
        var nested = root.Path;
        for (var i = 0; i < 20; i++)
        {
            nested = Path.Combine(nested, $"segment{i:00}");
        }

        try
        {
            Directory.CreateDirectory(nested);
            var file = Path.Combine(nested, "  Long   NAME .package");
            await File.WriteAllTextAsync(file, "x");
        }
        catch
        {
            return;
        }

        var result = await CreateEngine().TransformAsync(
            new TransformationOptions
            {
                SourcePath = root.Path,
                Recursive = true
            },
            TransformationMode.Normalize);

        Assert.True(result.TotalFiles >= 1);
    }

    private static UnifiedFileTransformationEngine CreateEngine()
    {
        var fileService = new CrossPlatformFileOperationService(NullLogger<CrossPlatformFileOperationService>.Instance);
        var hashService = new CrossPlatformHashComputationService(NullLogger<CrossPlatformHashComputationService>.Instance);
        var config = new CrossPlatformConfigurationProvider(
            NullLogger<CrossPlatformConfigurationProvider>.Instance,
            Path.Combine(Path.GetTempPath(), $"sims-config-{Guid.NewGuid():N}.json"));

        return new UnifiedFileTransformationEngine(
            NullLogger<UnifiedFileTransformationEngine>.Instance,
            fileService,
            hashService,
            config);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
