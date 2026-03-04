using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Services;
using SimsModDesktop.Infrastructure.Services;

namespace SimsModDesktop.Tests;

public sealed class CrossPlatformHashComputationServiceTests
{
    [Fact]
    public async Task ComputeFileHashesAsync_BatchLogsConfiguredWorkerCount()
    {
        using var tempDir = new TempDirectory("hash-batch");
        var filePaths = new[]
        {
            Path.Combine(tempDir.Path, "a.package"),
            Path.Combine(tempDir.Path, "b.package"),
            Path.Combine(tempDir.Path, "c.package")
        };
        foreach (var filePath in filePaths)
        {
            await File.WriteAllTextAsync(filePath, "hash-fixture");
        }

        var logger = new RecordingLogger<CrossPlatformHashComputationService>();
        var service = new CrossPlatformHashComputationService(logger);
        var results = await service.ComputeFileHashesAsync(new HashBatchRequest
        {
            FilePaths = filePaths,
            WorkerCount = 3
        });

        Assert.Equal(3, results.Count);
        Assert.Contains(logger.Messages, message => message.Contains("hash.batch.start", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("workerCount=3", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("hash.batch.done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ComputeFileHashesAsync_CancelledToken_StopsBatch()
    {
        using var tempDir = new TempDirectory("hash-cancel");
        var filePaths = Enumerable.Range(0, 20)
            .Select(index =>
            {
                var filePath = Path.Combine(tempDir.Path, $"f{index:D2}.package");
                File.WriteAllBytes(filePath, new byte[256 * 1024]);
                return filePath;
            })
            .ToArray();

        var service = new CrossPlatformHashComputationService(new RecordingLogger<CrossPlatformHashComputationService>());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.ComputeFileHashesAsync(
                new HashBatchRequest
                {
                    FilePaths = filePaths,
                    WorkerCount = 8
                },
                cancellationToken: cts.Token));
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
