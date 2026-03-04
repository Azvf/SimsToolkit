namespace SimsModDesktop.Application.Mods;

public sealed record ModIndexRefreshRequest
{
    public string ModsRootPath { get; init; } = string.Empty;
    public IReadOnlyList<string> ChangedPackages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RemovedPackages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PriorityPackages { get; init; } = Array.Empty<string>();
    public bool AllowDeepEnrichment { get; init; } = true;
}

public sealed record ModIndexRefreshProgress
{
    public string Stage { get; init; } = string.Empty;
    public int Percent { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
    public string Detail { get; init; } = string.Empty;
}
