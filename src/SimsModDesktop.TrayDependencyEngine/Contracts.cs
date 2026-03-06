using SimsModDesktop.PackageCore;

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
    public PackageIndexSnapshot? PreloadedSnapshot { get; init; }
    public int? CopyWorkerCount { get; init; }
    public bool RequireGraphFastPath { get; init; } = true;
}

public sealed record TrayDependencyAnalysisRequest
{
    public required string TrayPath { get; init; }
    public required string ModsRootPath { get; init; }
    public required string TrayItemKey { get; init; }
    public PackageIndexSnapshot? PreloadedSnapshot { get; init; }
    public int? CopyWorkerCount { get; init; }
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
    public TrayDependencyExportDiagnostics? Diagnostics { get; init; }
}

public sealed record TrayDependencyExportDiagnostics
{
    public int InputSourceFileCount { get; init; }
    public int BundleTrayItemFileCount { get; init; }
    public int BundleAuxiliaryFileCount { get; init; }
    public int CandidateResourceKeyCount { get; init; }
    public int CandidateIdCount { get; init; }
    public int SnapshotPackageCount { get; init; }
    public int DirectMatchCount { get; init; }
    public int ExpandedMatchCount { get; init; }
    public string ExpansionMode { get; init; } = "legacy";
    public int GraphVisitedNodes { get; init; }
    public long GraphVisitedEdges { get; init; }
    public int DbRoundTrips { get; init; }
    public long ExpandElapsedMs { get; init; }
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
    public IReadOnlyList<string> TrayItemPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> HhiPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SgiPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> HouseholdBinaryPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BlueprintPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RoomPaths { get; init; } = Array.Empty<string>();
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
    Task<PackageIndexSnapshot?> TryLoadSnapshotAsync(
        string modsRootPath,
        long inventoryVersion,
        CancellationToken cancellationToken = default);

    Task<PackageIndexSnapshot> BuildSnapshotAsync(
        PackageIndexBuildRequest request,
        IProgress<TrayDependencyExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record PackageIndexSnapshot
{
    public required string ModsRootPath { get; init; }
    public long InventoryVersion { get; init; }
    public required IReadOnlyList<IndexedPackageFile> Packages { get; init; }
    public PackageDependencyGraph? DependencyGraph { get; init; }
    public bool IsDependencyGraphReady { get; init; }
    public long GraphBuildVersion { get; init; }
    internal ITrayDependencyLookup Lookup { get; init; } = EmptyTrayDependencyLookup.Instance;
}

public sealed record PackageDependencyGraph
{
    public int PackageCount { get; init; }
    public required long[] EdgeOffsets { get; init; }
    public required int[] EdgeTargets { get; init; }
    public required string[] PackagePathsById { get; init; }
}

internal interface ITrayDependencyLookup
{
    ITrayDependencyLookupSession OpenSession();
}

internal interface ITrayDependencyLookupSession : IDisposable
{
    bool TryGetExact(TrayResourceKey key, out ResolvedResourceRef[] matches);

    bool TryGetTypeInstance(TypeInstanceKey key, out ResolvedResourceRef[] matches);

    bool HasSupportedInstance(ulong instance);
}

internal interface IBatchTrayDependencyLookupSession : ITrayDependencyLookupSession
{
    IReadOnlyDictionary<TrayResourceKey, ResolvedResourceRef[]> QueryExactBatch(IReadOnlyCollection<TrayResourceKey> keys);

    IReadOnlyDictionary<TypeInstanceKey, ResolvedResourceRef[]> QueryTypeInstanceBatch(IReadOnlyCollection<TypeInstanceKey> keys);

    IReadOnlyDictionary<ulong, bool> QuerySupportedInstanceBatch(IReadOnlyCollection<ulong> instances);
}

internal sealed class EmptyTrayDependencyLookup : ITrayDependencyLookup
{
    public static EmptyTrayDependencyLookup Instance { get; } = new();

    private EmptyTrayDependencyLookup()
    {
    }

    public ITrayDependencyLookupSession OpenSession() => EmptyTrayDependencyLookupSession.Instance;
}

internal sealed class EmptyTrayDependencyLookupSession : IBatchTrayDependencyLookupSession
{
    public static EmptyTrayDependencyLookupSession Instance { get; } = new();

    private EmptyTrayDependencyLookupSession()
    {
    }

    public bool TryGetExact(TrayResourceKey key, out ResolvedResourceRef[] matches)
    {
        matches = Array.Empty<ResolvedResourceRef>();
        return false;
    }

    public bool TryGetTypeInstance(TypeInstanceKey key, out ResolvedResourceRef[] matches)
    {
        matches = Array.Empty<ResolvedResourceRef>();
        return false;
    }

    public bool HasSupportedInstance(ulong instance) => false;

    public IReadOnlyDictionary<TrayResourceKey, ResolvedResourceRef[]> QueryExactBatch(IReadOnlyCollection<TrayResourceKey> keys)
    {
        return new Dictionary<TrayResourceKey, ResolvedResourceRef[]>();
    }

    public IReadOnlyDictionary<TypeInstanceKey, ResolvedResourceRef[]> QueryTypeInstanceBatch(IReadOnlyCollection<TypeInstanceKey> keys)
    {
        return new Dictionary<TypeInstanceKey, ResolvedResourceRef[]>();
    }

    public IReadOnlyDictionary<ulong, bool> QuerySupportedInstanceBatch(IReadOnlyCollection<ulong> instances)
    {
        return new Dictionary<ulong, bool>();
    }

    public void Dispose()
    {
    }
}

public sealed record PackageIndexBuildRequest
{
    public required string ModsRootPath { get; init; }
    public long InventoryVersion { get; init; }
    public required IReadOnlyList<PackageIndexBuildFile> PackageFiles { get; init; }
    public IReadOnlyList<PackageIndexBuildFile> ChangedPackageFiles { get; init; } = Array.Empty<PackageIndexBuildFile>();
    public IReadOnlyList<string> RemovedPackagePaths { get; init; } = Array.Empty<string>();
    public int? ParseWorkerCount { get; init; }
    public int? WriteBatchSize { get; init; }
    public int? ParseResultChannelCapacity { get; init; }
    public int? CommitBatchSize { get; init; }
    public int? CommitIntervalMs { get; init; }
    public bool? UseAdaptiveThrottleV2 { get; init; }
    public bool? UseIncrementalOrphanCleanup { get; init; }
    public bool ForceFullOrphanCleanup { get; init; }
    public bool? UseParallelPipeline { get; init; }
    public bool BuildDependencyGraph { get; init; } = true;
    public TrayPerformanceTier PerformanceTier { get; init; } = TrayPerformanceTier.High;
}

public enum TrayPerformanceTier
{
    Balanced,
    High
}

public sealed record PackageIndexBuildFile
{
    public required string FilePath { get; init; }
    public long Length { get; init; }
    public long LastWriteUtcTicks { get; init; }
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
