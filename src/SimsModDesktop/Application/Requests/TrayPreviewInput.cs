namespace SimsModDesktop.Application.Requests;

public sealed record TrayPreviewInput
{
    public required string TrayPath { get; init; }
    public string TrayItemKey { get; init; } = string.Empty;
    public int? TopN { get; init; }
    public int MaxFilesPerItem { get; init; } = 12;
    public int PageSize { get; init; } = 50;
}
