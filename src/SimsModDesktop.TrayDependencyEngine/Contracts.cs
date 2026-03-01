namespace SimsModDesktop.TrayDependencyEngine;

public interface ITrayDependencyExportService
{
    Task<TrayDependencyExportResult> ExportAsync(
        TrayDependencyExportRequest request,
        IProgress<TrayDependencyExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface ITrayDependencyAnalysisService
{
    Task<TrayDependencyAnalysisResult> AnalyzeAsync(
        TrayDependencyAnalysisRequest request,
        IProgress<TrayDependencyAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record TrayDependencyExportRequest
{
    public required string ItemTitle { get; init; }
    public required string TrayItemKey { get; init; }
    public required string TrayRootPath { get; init; }
    public required IReadOnlyList<string> TraySourceFiles { get; init; }
    public required string ModsRootPath { get; init; }
    public required string TrayExportRoot { get; init; }
    public required string ModsExportRoot { get; init; }
}

public sealed record TrayDependencyAnalysisRequest
{
    public required string TrayPath { get; init; }
    public required string ModsRootPath { get; init; }
    public required string TrayItemKey { get; init; }
    public int? MinMatchCount { get; init; }
    public int? TopN { get; init; }
    public int? MaxPackageCount { get; init; }
    public bool ExportUnusedPackages { get; init; }
    public bool ExportMatchedPackages { get; init; }
    public string? OutputCsv { get; init; }
    public string? UnusedOutputCsv { get; init; }
    public string? ExportTargetPath { get; init; }
    public string ExportMinConfidence { get; init; } = "Low";
}

public sealed record TrayDependencyExportResult
{
    public bool Success { get; init; }
    public int CopiedTrayFileCount { get; init; }
    public int CopiedModFileCount { get; init; }
    public bool HasMissingReferenceWarnings { get; init; }
    public IReadOnlyList<TrayDependencyIssue> Issues { get; init; } = Array.Empty<TrayDependencyIssue>();
}

public sealed record TrayDependencyAnalysisResult
{
    public bool Success { get; init; }
    public int MatchedPackageCount { get; init; }
    public int UnusedPackageCount { get; init; }
    public int ExportedMatchedPackageCount { get; init; }
    public int ExportedUnusedPackageCount { get; init; }
    public string? OutputCsvPath { get; init; }
    public string? UnusedOutputCsvPath { get; init; }
    public string? MatchedExportPath { get; init; }
    public string? UnusedExportPath { get; init; }
    public IReadOnlyList<TrayDependencyAnalysisRow> MatchedPackages { get; init; } = Array.Empty<TrayDependencyAnalysisRow>();
    public IReadOnlyList<TrayDependencyAnalysisRow> UnusedPackages { get; init; } = Array.Empty<TrayDependencyAnalysisRow>();
    public IReadOnlyList<TrayDependencyIssue> Issues { get; init; } = Array.Empty<TrayDependencyIssue>();
}

public sealed record TrayDependencyExportProgress
{
    public TrayDependencyExportStage Stage { get; init; }
    public int Percent { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed record TrayDependencyAnalysisProgress
{
    public TrayDependencyAnalysisStage Stage { get; init; }
    public int Percent { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public enum TrayDependencyExportStage
{
    Preparing,
    IndexingPackages,
    ParsingTray,
    MatchingDirectReferences,
    ExpandingDependencies,
    CopyingMods,
    Completed
}

public enum TrayDependencyAnalysisStage
{
    Preparing,
    IndexingPackages,
    ParsingTray,
    MatchingDirectReferences,
    ExpandingDependencies,
    WritingOutputs,
    Completed
}

public sealed record TrayDependencyIssue
{
    public TrayDependencyIssueSeverity Severity { get; init; }
    public TrayDependencyIssueKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? FilePath { get; init; }
    public string? ResourceKey { get; init; }
}

public sealed record TrayDependencyAnalysisRow
{
    public required string PackagePath { get; init; }
    public long PackageSizeBytes { get; init; }
    public int DirectMatchCount { get; init; }
    public int TransitiveMatchCount { get; init; }
    public int MatchInstanceCount { get; init; }
    public double MatchRatePct { get; init; }
    public string Confidence { get; init; } = "Low";
    public bool IsUnused { get; init; }
}

public enum TrayDependencyIssueSeverity
{
    Warning,
    Error
}

public enum TrayDependencyIssueKind
{
    MissingReference,
    MissingSourceFile,
    TrayParseError,
    PackageParseError,
    CacheBuildError,
    CopyError,
    InternalError
}

public sealed record TrayFileBundle
{
    public string TrayItemPath { get; init; } = string.Empty;
    public string? HhiPath { get; init; }
    public IReadOnlyList<string> SgiPaths { get; init; } = Array.Empty<string>();
    public string? HouseholdBinaryPath { get; init; }
    public string? BlueprintPath { get; init; }
    public string? RoomPath { get; init; }
}

public sealed record TraySearchKeys
{
    public ulong[] CasPartIds { get; init; } = Array.Empty<ulong>();
    public ulong[] SkinToneIds { get; init; } = Array.Empty<ulong>();
    public ulong[] SimAspirationIds { get; init; } = Array.Empty<ulong>();
    public ulong[] SimTraitIds { get; init; } = Array.Empty<ulong>();
    public ulong[] CasPresetIds { get; init; } = Array.Empty<ulong>();
    public ulong[] FaceSliderIds { get; init; } = Array.Empty<ulong>();
    public ulong[] BodySliderIds { get; init; } = Array.Empty<ulong>();
    public ulong[] ObjectDefinitionIds { get; init; } = Array.Empty<ulong>();
    public TrayResourceKey[] ResourceKeys { get; init; } = Array.Empty<TrayResourceKey>();
    public ulong[] LotTraitIds { get; init; } = Array.Empty<ulong>();
}

public sealed record TrayResourceKey(uint Type, uint Group, ulong Instance);

public interface IPackageIndexCache
{
    Task<PackageIndexSnapshot> GetSnapshotAsync(
        string modsRootPath,
        IProgress<TrayDependencyExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record PackageIndexSnapshot
{
    public required string ModsRootPath { get; init; }
    public required IReadOnlyList<IndexedPackageFile> Packages { get; init; }
}

public sealed record IndexedPackageFile
{
    public required string FilePath { get; init; }
    public long Length { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public required PackageIndexEntry[] Entries { get; init; }
    public required IReadOnlyDictionary<uint, PackageTypeIndex> TypeIndexes { get; init; }
}

public sealed record PackageIndexEntry
{
    public uint Type { get; init; }
    public uint Group { get; init; }
    public ulong Instance { get; init; }
    public bool IsDeleted { get; init; }
    public long DataOffset { get; init; }
    public int CompressedSize { get; init; }
    public int UncompressedSize { get; init; }
    public ushort CompressionType { get; init; }
}

public sealed record PackageTypeIndex
{
    public required IReadOnlyDictionary<ulong, int[]> InstanceToEntryIndexes { get; init; }
}

public sealed record ResolvedResourceRef
{
    public required TrayResourceKey Key { get; init; }
    public required string FilePath { get; init; }
    public PackageIndexEntry? Entry { get; init; }
    public ResolvedResourceRef? Parent { get; init; }
}
