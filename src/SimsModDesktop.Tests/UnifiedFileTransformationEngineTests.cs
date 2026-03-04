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

        var provider = new CrossPlatformConfigurationProvider(
            NullLogger<CrossPlatformConfigurationProvider>.Instance,
            Path.Combine(Path.GetTempPath(), $"sims-config-{Guid.NewGuid():N}.json"));
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
    public async Task Merge_WithDifferentWorkerCounts_ProducesEquivalentOutput()
    {
        using var root = new TempDirectory("merge-workers-root");
        var sourceA = Path.Combine(root.Path, "mods-a");
        var sourceB = Path.Combine(root.Path, "mods-b");
        var targetA = Path.Combine(root.Path, "target-a");
        var targetB = Path.Combine(root.Path, "target-b");
        Directory.CreateDirectory(sourceA);
        Directory.CreateDirectory(sourceB);
        Directory.CreateDirectory(Path.Combine(sourceA, "sub1"));
        Directory.CreateDirectory(Path.Combine(sourceB, "sub2"));

        await File.WriteAllTextAsync(Path.Combine(sourceA, "sub1", "a.package"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(sourceA, "root-a.package"), "root-a");
        await File.WriteAllTextAsync(Path.Combine(sourceB, "sub2", "b.package"), "beta");
        await File.WriteAllTextAsync(Path.Combine(sourceB, "root-b.package"), "root-b");

        var sut = CreateEngine();
        var mergeModeOptions = new ModeSpecificOptions
        {
            Merge = new MergeOptions
            {
                SourcePaths = [sourceA, sourceB]
            }
        };

        var serialResult = await sut.TransformAsync(
            new TransformationOptions
            {
                SourcePath = sourceA,
                TargetPath = targetA,
                Recursive = true,
                KeepSource = true,
                WorkerCount = 1,
                ModeOptions = mergeModeOptions
            },
            TransformationMode.Merge);
        var parallelResult = await sut.TransformAsync(
            new TransformationOptions
            {
                SourcePath = sourceA,
                TargetPath = targetB,
                Recursive = true,
                KeepSource = true,
                WorkerCount = 8,
                ModeOptions = mergeModeOptions
            },
            TransformationMode.Merge);

        Assert.True(serialResult.Success);
        Assert.True(parallelResult.Success);

        var serialFiles = Directory.EnumerateFiles(targetA, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(targetA, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var parallelFiles = Directory.EnumerateFiles(targetB, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(targetB, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(serialFiles, parallelFiles);

        foreach (var relativePath in serialFiles)
        {
            var left = await File.ReadAllTextAsync(Path.Combine(targetA, relativePath));
            var right = await File.ReadAllTextAsync(Path.Combine(targetB, relativePath));
            Assert.Equal(left, right);
        }
    }

    [Fact]
    public async Task Merge_WhenCancelled_StopsWorkerPool()
    {
        using var root = new TempDirectory("merge-cancel-root");
        var source = Path.Combine(root.Path, "mods");
        var target = Path.Combine(root.Path, "target");
        Directory.CreateDirectory(source);

        for (var i = 0; i < 200; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(source, $"f{i:D3}.package"), $"fixture-{i}");
        }

        var sut = CreateEngine();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(1);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await sut.TransformAsync(
                new TransformationOptions
                {
                    SourcePath = source,
                    TargetPath = target,
                    Recursive = true,
                    KeepSource = true,
                    WorkerCount = 8,
                    ModeOptions = new ModeSpecificOptions
                    {
                        Merge = new MergeOptions
                        {
                            SourcePaths = [source]
                        }
                    }
                },
                TransformationMode.Merge,
                cancellationToken: cts.Token));
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
        var config = provider ?? new CrossPlatformConfigurationProvider(
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
