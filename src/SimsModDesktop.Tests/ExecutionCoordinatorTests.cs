using SimsModDesktop.Application.Cli;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class ExecutionCoordinatorTests
{
    [Fact]
    public void Constructor_NoStrategies_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ExecutionCoordinator(
                new FakeRunner(),
                new FakeTransformationEngine(),
                new FixedRoutingPolicy(),
                NullLogger<ExecutionCoordinator>.Instance,
                Array.Empty<IActionExecutionStrategy>()));

        Assert.Contains("No execution strategies were registered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_DuplicateStrategies_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
                new ExecutionCoordinator(
                    new FakeRunner(),
                    new FakeTransformationEngine(),
                    new FixedRoutingPolicy(),
                    NullLogger<ExecutionCoordinator>.Instance,
                    new IActionExecutionStrategy[]
                    {
                        new FakeStrategy(SimsAction.Organize),
                        new FakeStrategy(SimsAction.Organize)
                    }));

        Assert.Contains("Duplicate execution strategies were registered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedFails_FallsBackToPowerShell()
    {
        using var tempDir = new TempDirectory();
        var source = Path.Combine(tempDir.Path, "mods");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "a.package"), "x");

        var runner = new FakeRunner();
        var transformation = new FakeTransformationEngine
        {
            NextResult = new TransformationResult
            {
                Success = false,
                ErrorMessage = "boom"
            }
        };
        var coordinator = new ExecutionCoordinator(
            runner,
            transformation,
            new FixedRoutingPolicy(new EngineRoutingDecision
            {
                UseUnifiedEngine = true,
                EnableFallbackToPowerShell = true,
                FallbackOnValidationFailure = false
            }),
            NullLogger<ExecutionCoordinator>.Instance,
            [new FakeStrategy(SimsAction.Flatten)]);

        var result = await coordinator.ExecuteAsync(
            new FlattenInput
            {
                ScriptPath = Path.Combine(tempDir.Path, "script.ps1"),
                FlattenRootPath = source,
                Shared = new SharedFileOpsInput()
            },
            _ => { });

        Assert.Equal("pwsh", result.Executable);
        Assert.True(runner.Calls > 0);
        Assert.Equal(1, transformation.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedSuccess_ReturnsUnifiedResult()
    {
        using var tempDir = new TempDirectory();
        var source = Path.Combine(tempDir.Path, "normalize");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "a.package"), "x");

        var runner = new FakeRunner();
        var transformation = new FakeTransformationEngine
        {
            NextResult = new TransformationResult
            {
                Success = true,
                ProcessedFiles = 1
            }
        };
        var coordinator = new ExecutionCoordinator(
            runner,
            transformation,
            new FixedRoutingPolicy(new EngineRoutingDecision
            {
                UseUnifiedEngine = true,
                EnableFallbackToPowerShell = true,
                FallbackOnValidationFailure = false
            }),
            NullLogger<ExecutionCoordinator>.Instance,
            [new FakeStrategy(SimsAction.Normalize)]);

        var result = await coordinator.ExecuteAsync(
            new NormalizeInput
            {
                ScriptPath = Path.Combine(tempDir.Path, "script.ps1"),
                NormalizeRootPath = source
            },
            _ => { });

        Assert.Equal("UnifiedFileTransformationEngine", result.Executable);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(1, transformation.Calls);
    }

    private sealed class FakeRunner : ISimsPowerShellRunner
    {
        public int Calls { get; private set; }

        public Task<SimsExecutionResult> RunAsync(SimsProcessCommand command, Action<string> onOutput, Action<SimsProgressUpdate>? onProgress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new SimsExecutionResult
            {
                ExitCode = 0,
                Executable = "pwsh",
                Arguments = command.Arguments
            });
        }
    }

    private sealed class FakeStrategy : IActionExecutionStrategy
    {
        public FakeStrategy(SimsAction action)
        {
            Action = action;
        }

        public SimsAction Action { get; }

        public bool TryValidate(ISimsExecutionInput input, out string error)
        {
            error = string.Empty;
            return true;
        }

        public SimsProcessCommand BuildCommand(ISimsExecutionInput input)
        {
            return new SimsProcessCommand
            {
                Arguments = ["-Action", Action.ToString()]
            };
        }
    }

    private sealed class FixedRoutingPolicy : IExecutionEngineRoutingPolicy
    {
        private readonly EngineRoutingDecision _decision;

        public FixedRoutingPolicy(EngineRoutingDecision? decision = null)
        {
            _decision = decision ?? new EngineRoutingDecision
            {
                UseUnifiedEngine = false,
                EnableFallbackToPowerShell = true,
                FallbackOnValidationFailure = false
            };
        }

        public Task<EngineRoutingDecision> DecideAsync(SimsAction action, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_decision);
        }
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
