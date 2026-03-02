using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Infrastructure.Services;

namespace SimsModDesktop.Tests;

public sealed class UnifiedFileTransformationEngineTests
{
    [Fact]
    public async Task Flatten_WithSkipConflict_KeepsTargetAndMarksSkipped()
    {
        using var source = new TempDirectory("flatten-src");
        using var target = new TempDirectory("flatten-dst");

        var sourceFile = Path.Combine(source.Path, "a.package");
        var targetFile = Path.Combine(target.Path, "a.package");
        await File.WriteAllTextAsync(sourceFile, "source");
        await File.WriteAllTextAsync(targetFile, "target");

        var sut = CreateEngine();
        var result = await sut.TransformAsync(
            new TransformationOptions
            {
                SourcePath = source.Path,
                TargetPath = target.Path,
                Recursive = true,
                ConflictStrategy = ConflictResolutionStrategy.Skip
            },
            TransformationMode.Flatten);

        Assert.True(result.Success);
        Assert.Equal(0, result.ProcessedFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal("target", await File.ReadAllTextAsync(targetFile));
        Assert.True(File.Exists(sourceFile));
    }

    [Fact]
    public async Task Flatten_WithOverwriteConflict_ReplacesTarget()
    {
        using var source = new TempDirectory("flatten-src");
        using var target = new TempDirectory("flatten-dst");

        var sourceFile = Path.Combine(source.Path, "a.package");
        var targetFile = Path.Combine(target.Path, "a.package");
        await File.WriteAllTextAsync(sourceFile, "source");
        await File.WriteAllTextAsync(targetFile, "target");

        var sut = CreateEngine();
        var result = await sut.TransformAsync(
            new TransformationOptions
            {
                SourcePath = source.Path,
                TargetPath = target.Path,
                Recursive = true,
                ConflictStrategy = ConflictResolutionStrategy.Overwrite
            },
            TransformationMode.Flatten);

        Assert.True(result.Success);
        Assert.Equal(1, result.ProcessedFiles);
        Assert.Equal("source", await File.ReadAllTextAsync(targetFile));
        Assert.False(File.Exists(sourceFile));
    }

    [Fact]
    public async Task Flatten_WithWhatIf_DoesNotModifyFiles()
    {
        using var source = new TempDirectory("flatten-src");
        using var target = new TempDirectory("flatten-dst");

        var sourceFile = Path.Combine(source.Path, "a.package");
        var targetFile = Path.Combine(target.Path, "a.package");
        await File.WriteAllTextAsync(sourceFile, "source");
        await File.WriteAllTextAsync(targetFile, "target");

        var sut = CreateEngine();
        var result = await sut.TransformAsync(
            new TransformationOptions
            {
                SourcePath = source.Path,
                TargetPath = target.Path,
                Recursive = true,
                ConflictStrategy = ConflictResolutionStrategy.Overwrite,
                WhatIf = true
            },
            TransformationMode.Flatten);

        Assert.True(result.Success);
        Assert.Equal(1, result.ProcessedFiles);
        Assert.Equal("target", await File.ReadAllTextAsync(targetFile));
        Assert.True(File.Exists(sourceFile));
    }

    [Fact]
    public async Task Normalize_CompressesWhitespace_AndLowercases_WhenConfigured()
    {
        using var root = new TempDirectory("normalize-root");
        var filePath = Path.Combine(root.Path, "  My   FILE .package");
        await File.WriteAllTextAsync(filePath, "x");

        var provider = new CrossPlatformConfigurationProvider(NullLogger<CrossPlatformConfigurationProvider>.Instance);
        await provider.SetConfigurationAsync("Normalize.CasePolicy", "lower");
        var sut = CreateEngine(provider);

        var result = await sut.TransformAsync(
            new TransformationOptions
            {
                SourcePath = root.Path,
                Recursive = true
            },
            TransformationMode.Normalize);

        Assert.True(result.Success);
        Assert.Equal(1, result.ProcessedFiles);
        Assert.True(File.Exists(Path.Combine(root.Path, "my file.package")));
    }

    [Fact]
    public async Task Merge_WithOverlappingPath_WarnsAndContinues_ByDefault()
    {
        using var root = new TempDirectory("merge-root");
        var source = Path.Combine(root.Path, "mods");
        var target = Path.Combine(source, "merged");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "a.package"), "x");

        var sut = CreateEngine();
        var result = await sut.TransformAsync(
            new TransformationOptions
            {
                SourcePath = source,
                TargetPath = target,
                Recursive = true,
                KeepSource = true,
                ModeOptions = new ModeSpecificOptions
                {
                    Merge = new MergeOptions
                    {
                        SourcePaths = [source]
                    }
                }
            },
            TransformationMode.Merge);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
        Assert.True(File.Exists(Path.Combine(target, "a.package")));
    }

    [Fact]
    public async Task Organize_ExtractsZip_AndDeletesZip_WhenKeepZipFalse()
    {
        using var root = new TempDirectory("organize-root");
        var sourceContentDir = Path.Combine(root.Path, "payload");
        Directory.CreateDirectory(sourceContentDir);
        await File.WriteAllTextAsync(Path.Combine(sourceContentDir, "mod.package"), "x");

        var zipPath = Path.Combine(root.Path, "pack.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(sourceContentDir, zipPath);

        var target = Path.Combine(root.Path, "out");
        var sut = CreateEngine();
        var result = await sut.TransformAsync(
            new TransformationOptions
            {
                SourcePath = root.Path,
                TargetPath = target,
                ModeOptions = new ModeSpecificOptions
                {
                    Organize = new OrganizeOptions
                    {
                        KeepZip = false
                    }
                }
            },
            TransformationMode.Organize);

        Assert.True(result.Success);
        Assert.False(File.Exists(zipPath));
        Assert.True(Directory.EnumerateFiles(target, "mod.package", SearchOption.AllDirectories).Any());
    }

    private static UnifiedFileTransformationEngine CreateEngine(CrossPlatformConfigurationProvider? provider = null)
    {
        var fileService = new CrossPlatformFileOperationService(NullLogger<CrossPlatformFileOperationService>.Instance);
        var hashService = new CrossPlatformHashComputationService(NullLogger<CrossPlatformHashComputationService>.Instance);
        var config = provider ?? new CrossPlatformConfigurationProvider(NullLogger<CrossPlatformConfigurationProvider>.Instance);
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

public sealed class CrossPlatformFileOperationServiceTests
{
    [Fact]
    public async Task DeleteFileAsync_Permanent_DeletesFile()
    {
        var sut = new CrossPlatformFileOperationService(NullLogger<CrossPlatformFileOperationService>.Instance);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "x");

        try
        {
            var ok = await sut.DeleteFileAsync(path, permanent: true);
            Assert.True(ok);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task DeleteDirectoryAsync_Permanent_DeletesDirectory()
    {
        var sut = new CrossPlatformFileOperationService(NullLogger<CrossPlatformFileOperationService>.Instance);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(System.IO.Path.Combine(path, "a.txt"), "x");

        var ok = await sut.DeleteDirectoryAsync(path, permanent: true);
        Assert.True(ok);
        Assert.False(Directory.Exists(path));
    }
}
