using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;

namespace SimsModDesktop.Application.Execution;

public sealed class TrayPreviewRunner : ITrayPreviewRunner
{
    private readonly ITrayPreviewCoordinator _trayPreviewCoordinator;

    public TrayPreviewRunner(ITrayPreviewCoordinator trayPreviewCoordinator)
    {
        _trayPreviewCoordinator = trayPreviewCoordinator;
    }

    public async Task<TrayPreviewDashboardRunResult> LoadDashboardAsync(
        TrayPreviewInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            var result = await _trayPreviewCoordinator.LoadAsync(input, cancellationToken);
            return new TrayPreviewDashboardRunResult
            {
                Status = ExecutionRunStatus.Success,
                LoadResult = result
            };
        }
        catch (OperationCanceledException)
        {
            return new TrayPreviewDashboardRunResult
            {
                Status = ExecutionRunStatus.Cancelled
            };
        }
        catch (Exception ex)
        {
            return new TrayPreviewDashboardRunResult
            {
                Status = ExecutionRunStatus.Failed,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<TrayPreviewPageRunResult> LoadPageAsync(
        int requestedPageIndex,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _trayPreviewCoordinator.LoadPageAsync(requestedPageIndex, cancellationToken);
            return new TrayPreviewPageRunResult
            {
                Status = ExecutionRunStatus.Success,
                PageResult = result
            };
        }
        catch (OperationCanceledException)
        {
            return new TrayPreviewPageRunResult
            {
                Status = ExecutionRunStatus.Cancelled
            };
        }
        catch (Exception ex)
        {
            return new TrayPreviewPageRunResult
            {
                Status = ExecutionRunStatus.Failed,
                ErrorMessage = ex.Message
            };
        }
    }

    public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(input);
        return _trayPreviewCoordinator.TryGetCached(input, out result);
    }

    public void Reset()
    {
        _trayPreviewCoordinator.Reset();
    }
}
