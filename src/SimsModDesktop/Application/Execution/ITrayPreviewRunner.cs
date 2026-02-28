using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;

namespace SimsModDesktop.Application.Execution;

public interface ITrayPreviewRunner
{
    Task<TrayPreviewLoadRunResult> LoadPreviewAsync(
        TrayPreviewInput input,
        CancellationToken cancellationToken = default);

    Task<TrayPreviewPageRunResult> LoadPageAsync(
        int requestedPageIndex,
        CancellationToken cancellationToken = default);

    bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result);

    void Reset();
}

public sealed record TrayPreviewLoadRunResult
{
    public ExecutionRunStatus Status { get; init; }
    public TrayPreviewLoadResult? LoadResult { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}

public sealed record TrayPreviewPageRunResult
{
    public ExecutionRunStatus Status { get; init; }
    public TrayPreviewPageResult? PageResult { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}

