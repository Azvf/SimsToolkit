using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Models;

namespace SimsModDesktop.Tests;

public sealed class ExecutionRunnersTests
{
    [Fact]
    public async Task ToolkitExecutionRunner_Success_ReturnsSuccess()
    {
        var coordinator = new FakeExecutionCoordinator
        {
            NextResult = new SimsExecutionResult
            {
                ExitCode = 0,
                Executable = "pwsh",
                Arguments = ["-NoProfile"]
            }
        };
        var runner = new ToolkitExecutionRunner(coordinator);
        var plan = new CliExecutionPlan(new OrganizeInput
        {
            ScriptPath = @"C:\tools\sims-mod-cli.ps1"
        });

        var result = await runner.RunAsync(plan, _ => { });

        Assert.Equal(ExecutionRunStatus.Success, result.Status);
        Assert.NotNull(result.ExecutionResult);
        Assert.Equal(0, result.ExecutionResult!.ExitCode);
    }

    [Fact]
    public async Task ToolkitExecutionRunner_Cancelled_ReturnsCancelled()
    {
        var coordinator = new FakeExecutionCoordinator
        {
            ExceptionToThrow = new OperationCanceledException()
        };
        var runner = new ToolkitExecutionRunner(coordinator);
        var plan = new CliExecutionPlan(new OrganizeInput
        {
            ScriptPath = @"C:\tools\sims-mod-cli.ps1"
        });

        var result = await runner.RunAsync(plan, _ => { });

        Assert.Equal(ExecutionRunStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task ToolkitExecutionRunner_Failure_ReturnsFailedWithMessage()
    {
        var coordinator = new FakeExecutionCoordinator
        {
            ExceptionToThrow = new InvalidOperationException("boom")
        };
        var runner = new ToolkitExecutionRunner(coordinator);
        var plan = new CliExecutionPlan(new OrganizeInput
        {
            ScriptPath = @"C:\tools\sims-mod-cli.ps1"
        });

        var result = await runner.RunAsync(plan, _ => { });

        Assert.Equal(ExecutionRunStatus.Failed, result.Status);
        Assert.Equal("boom", result.ErrorMessage);
    }

    [Fact]
    public async Task TrayPreviewRunner_Success_ReturnsPreviewResult()
    {
        var coordinator = new FakeTrayPreviewCoordinator();
        var runner = new TrayPreviewRunner(coordinator);

        var result = await runner.LoadPreviewAsync(new TrayPreviewInput
        {
            TrayPath = Path.GetTempPath(),
            PageSize = 50
        });

        Assert.Equal(ExecutionRunStatus.Success, result.Status);
        Assert.NotNull(result.LoadResult);
    }

    [Fact]
    public async Task TrayPreviewRunner_PageFailure_ReturnsFailed()
    {
        var coordinator = new FakeTrayPreviewCoordinator
        {
            PageExceptionToThrow = new InvalidOperationException("page failed")
        };
        var runner = new TrayPreviewRunner(coordinator);

        var result = await runner.LoadPageAsync(2);

        Assert.Equal(ExecutionRunStatus.Failed, result.Status);
        Assert.Equal("page failed", result.ErrorMessage);
    }

    private sealed class FakeExecutionCoordinator : IExecutionCoordinator
    {
        public SimsExecutionResult? NextResult { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public Task<SimsExecutionResult> ExecuteAsync(
            ISimsExecutionInput input,
            Action<string> onOutput,
            Action<SimsProgressUpdate>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(NextResult ?? new SimsExecutionResult
            {
                ExitCode = 0,
                Executable = "pwsh",
                Arguments = Array.Empty<string>()
            });
        }
    }

    private sealed class FakeTrayPreviewCoordinator : ITrayPreviewCoordinator
    {
        public Exception? LoadExceptionToThrow { get; set; }
        public Exception? PageExceptionToThrow { get; set; }

        public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
        {
            result = null!;
            return false;
        }

        public Task<TrayPreviewLoadResult> LoadAsync(TrayPreviewInput input, CancellationToken cancellationToken = default)
        {
            if (LoadExceptionToThrow is not null)
            {
                throw LoadExceptionToThrow;
            }

            return Task.FromResult(new TrayPreviewLoadResult
            {
                Summary = new SimsTrayPreviewSummary(),
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = 1,
                    PageSize = input.PageSize,
                    TotalItems = 0,
                    TotalPages = 1,
                    Items = Array.Empty<SimsTrayPreviewItem>()
                },
                LoadedPageCount = 1
            });
        }

        public Task<TrayPreviewPageResult> LoadPageAsync(int requestedPageIndex, CancellationToken cancellationToken = default)
        {
            if (PageExceptionToThrow is not null)
            {
                throw PageExceptionToThrow;
            }

            return Task.FromResult(new TrayPreviewPageResult
            {
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = requestedPageIndex,
                    PageSize = 50,
                    TotalItems = 0,
                    TotalPages = 1,
                    Items = Array.Empty<SimsTrayPreviewItem>()
                },
                LoadedPageCount = 1,
                FromCache = false
            });
        }

        public void Reset()
        {
        }
    }
}

