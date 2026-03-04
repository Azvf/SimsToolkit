using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Services;
using SimsModDesktop.Application.Validation;
using Microsoft.Extensions.Logging.Abstractions;

namespace SimsModDesktop.Tests;

public sealed class ExecutionCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_Transformation_ReturnsUnifiedResult()
    {
        using var tempDir = new TempDirectory();
        var source = Path.Combine(tempDir.Path, "normalize");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "a.package"), "x");

        var transformation = new FakeTransformationEngine
        {
            NextResult = new TransformationResult
            {
                Success = true,
                ProcessedFiles = 1
            }
        };
        var coordinator = new ExecutionCoordinator(
            transformation,
            new FakeHashComputationService(),
            new FakeFileOperationService(),
            new FindDupInputValidator(new SharedFileOpsInputValidator()),
            NullLogger<ExecutionCoordinator>.Instance);

        var result = await coordinator.ExecuteAsync(
            new NormalizeInput
            {
                NormalizeRootPath = source
            },
            _ => { });

        Assert.Equal("UnifiedFileTransformationEngine", result.Executable);
        Assert.Equal(1, transformation.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_FindDuplicates_WritesCsvWithoutPowerShell()
    {
        using var tempDir = new TempDirectory();
        var root = Path.Combine(tempDir.Path, "mods");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "a.package"), "same");
        await File.WriteAllTextAsync(Path.Combine(root, "b.package"), "same");
        await File.WriteAllTextAsync(Path.Combine(root, "c.package"), "different");
        var csvPath = Path.Combine(tempDir.Path, "dups", "duplicates.csv");

        var coordinator = new ExecutionCoordinator(
            new FakeTransformationEngine(),
            new FakeHashComputationService(),
            new FakeFileOperationService(),
            new FindDupInputValidator(new SharedFileOpsInputValidator()),
            NullLogger<ExecutionCoordinator>.Instance);

        var log = new List<string>();
        var result = await coordinator.ExecuteAsync(
            new FindDupInput
            {
                FindDupRootPath = root,
                FindDupOutputCsv = csvPath,
                FindDupRecurse = true,
                FindDupCleanup = false,
                Shared = new SharedFileOpsInput
                {
                    ModFilesOnly = true,
                    ModExtensions = [".package"]
                }
            },
            log.Add);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("FindDuplicateFiles", result.Executable);
        Assert.True(File.Exists(csvPath));
        var csv = await File.ReadAllTextAsync(csvPath);
        Assert.Contains("FilePath,Md5Hash,FileSize,GroupId,FileCount", csv, StringComparison.Ordinal);
        Assert.Contains(log, line => line.Contains("Exported to:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_FindDuplicates_CleanupDeletesDuplicates()
    {
        using var tempDir = new TempDirectory();
        var root = Path.Combine(tempDir.Path, "mods");
        Directory.CreateDirectory(root);
        var keep = Path.Combine(root, "a.package");
        var duplicate = Path.Combine(root, "b.package");
        await File.WriteAllTextAsync(keep, "same");
        await File.WriteAllTextAsync(duplicate, "same");

        var fileOperations = new FakeFileOperationService();
        var coordinator = new ExecutionCoordinator(
            new FakeTransformationEngine(),
            new FakeHashComputationService(),
            fileOperations,
            new FindDupInputValidator(new SharedFileOpsInputValidator()),
            NullLogger<ExecutionCoordinator>.Instance);

        var result = await coordinator.ExecuteAsync(
            new FindDupInput
            {
                FindDupRootPath = root,
                FindDupRecurse = true,
                FindDupCleanup = true,
                Shared = new SharedFileOpsInput()
            },
            _ => { });

        Assert.Equal(0, result.ExitCode);
        Assert.Single(fileOperations.DeletedFiles);
        Assert.Contains(duplicate, fileOperations.DeletedFiles);
    }

    private sealed class FakeTransformationEngine : IFileTransformationEngine
    {
        public int Calls { get; private set; }
        public TransformationResult NextResult { get; set; } = new()
        {
            Success = true
        };

        public Task<TransformationResult> TransformAsync(TransformationOptions options, TransformationMode mode, IProgress<TransformationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(NextResult);
        }

        public ValidationResult ValidateOptions(TransformationOptions options)
        {
            return ValidationResult.Success();
        }

        public IReadOnlyList<TransformationMode> SupportedModes => [TransformationMode.Flatten, TransformationMode.Normalize, TransformationMode.Merge, TransformationMode.Organize];
        public TransformationEngineInfo EngineInfo => new()
        {
            Name = "fake",
            Version = "1.0.0",
            SupportedModes = SupportedModes
        };
    }

    private sealed class FakeHashComputationService : IHashComputationService
    {
        public IReadOnlyList<HashAlgorithm> SupportedAlgorithms => [HashAlgorithm.MD5];

        public Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
            => ComputeFileHashAsync(filePath, HashAlgorithm.MD5, cancellationToken);

        public async Task<string> ComputeFileHashAsync(string filePath, HashAlgorithm algorithm, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return Convert.ToHexString(bytes);
        }

        public async Task<string> ComputeFilePrefixHashAsync(string filePath, int prefixBytes, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return Convert.ToHexString(bytes.Take(prefixBytes).ToArray());
        }

        public async Task<bool> AreFilesIdenticalAsync(string path1, string path2, CancellationToken cancellationToken = default)
            => string.Equals(
                await ComputeFileHashAsync(path1, cancellationToken),
                await ComputeFileHashAsync(path2, cancellationToken),
                StringComparison.OrdinalIgnoreCase);

        public async Task<bool> AreFilePrefixesIdenticalAsync(string path1, string path2, int prefixBytes, CancellationToken cancellationToken = default)
            => string.Equals(
                await ComputeFilePrefixHashAsync(path1, prefixBytes, cancellationToken),
                await ComputeFilePrefixHashAsync(path2, prefixBytes, cancellationToken),
                StringComparison.OrdinalIgnoreCase);

        public async Task<IReadOnlyList<FileHashResult>> ComputeFileHashesAsync(IReadOnlyList<string> filePaths, IProgress<HashProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var results = new List<FileHashResult>(filePaths.Count);
            for (var i = 0; i < filePaths.Count; i++)
            {
                var path = filePaths[i];
                var hash = await ComputeFileHashAsync(path, cancellationToken);
                var fileInfo = new FileInfo(path);
                results.Add(new FileHashResult
                {
                    FilePath = path,
                    Hash = hash,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    ElapsedMilliseconds = 0
                });
                progress?.Report(new HashProgress
                {
                    ProcessedCount = i + 1,
                    TotalCount = filePaths.Count,
                    CurrentFile = path,
                    ProcessedBytes = fileInfo.Length,
                    TotalBytes = filePaths.Sum(item => new FileInfo(item).Length)
                });
            }

            return results;
        }

        public Task<IReadOnlyList<FileHashResult>> ComputeFilePrefixHashesAsync(IReadOnlyList<string> filePaths, int prefixBytes, IProgress<HashProgress>? progress = null, CancellationToken cancellationToken = default)
            => ComputeFileHashesAsync(filePaths, progress, cancellationToken);
    }

    private sealed class FakeFileOperationService : IFileOperationService
    {
        public List<string> DeletedFiles { get; } = [];

        public Task<bool> MoveToRecycleBinAsync(string path) => Task.FromResult(true);

        public Task<bool> DeleteFileAsync(string path, bool permanent = false)
        {
            DeletedFiles.Add(path);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return Task.FromResult(true);
        }

        public Task<bool> DeleteDirectoryAsync(string path, bool permanent = false) => Task.FromResult(true);

        public string NormalizePath(string path) => path;

        public string CombinePaths(params string[] paths) => Path.Combine(paths);

        public RecycleBinInfo? GetRecycleBinInfo() => null;

        public bool IsRecycleBinSupported => false;

        public PlatformID CurrentPlatform => Environment.OSVersion.Platform;

        public bool IsPathRooted(string path) => Path.IsPathRooted(path);

        public string GetRelativePath(string relativeTo, string path) => Path.GetRelativePath(relativeTo, path);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"exec-coord-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
