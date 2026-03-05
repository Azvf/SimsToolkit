using EA.Sims4.Persistence;
using ProtoBuf;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.SaveData.Services;

public sealed class SaveAppearanceLinkService : ISaveAppearanceLinkService, ITS4SimAppearanceService
{
    private const uint SaveDataResourceType = 0x0000000D;
    private const string IssueCodeResourceNotFound = "RESOURCE_NOT_FOUND";
    private const string IssueCodeResourceReadFailed = "RESOURCE_READ_FAILED";
    private const string IssueCodeParserFailed = "PARSER_FAILED";
    private const string IssueCodeMorphPayloadInvalid = "MORPH_PAYLOAD_INVALID";
    private const string IssueCodeMorphGraphParseFailed = "MORPH_GRAPH_PARSE_FAILED";
    private const string IssueCodePackageEnumerationFailed = "PACKAGE_ENUMERATION_FAILED";
    private const string IssueCodeSpeciesLimitation = "SPECIES_LIMITATION";

    private readonly IDbpfPackageCatalog _catalog;
    private readonly IDbpfResourceReader _resourceReader;
    private readonly ITS4ResourceLocator _locator;
    private readonly ITS4ResourceParser<Ts4SimInfoResource> _simInfoParser;
    private readonly Ts4MorphLinkGraphBuilder _morphGraphBuilder;
    private readonly ITS4ResourceParser<Ts4BgeoHeader> _bgeoHeaderParser;
    private readonly ITS4ResourceParser<Ts4DmapHeader> _dmapHeaderParser;
    private readonly ITS4ResourceParser<Ts4BondHeader> _bondHeaderParser;

    public SaveAppearanceLinkService(
        IDbpfPackageCatalog? catalog = null,
        IDbpfResourceReader? resourceReader = null,
        ITS4ResourceLocator? locator = null,
        ITS4ResourceParser<Ts4SimInfoResource>? simInfoParser = null,
        Ts4MorphLinkGraphBuilder? morphGraphBuilder = null,
        ITS4ResourceParser<Ts4BgeoHeader>? bgeoHeaderParser = null,
        ITS4ResourceParser<Ts4DmapHeader>? dmapHeaderParser = null,
        ITS4ResourceParser<Ts4BondHeader>? bondHeaderParser = null)
    {
        _catalog = catalog ?? new DbpfPackageCatalog();
        _resourceReader = resourceReader ?? new DbpfResourceReader();
        _locator = locator ?? new Ts4ResourceLocator();
        _simInfoParser = simInfoParser ?? new Ts4SimInfoResourceParser();
        _morphGraphBuilder = morphGraphBuilder ?? new Ts4MorphLinkGraphBuilder();
        _bgeoHeaderParser = bgeoHeaderParser ?? new Ts4BgeoHeaderParser();
        _dmapHeaderParser = dmapHeaderParser ?? new Ts4DmapHeaderParser();
        _bondHeaderParser = bondHeaderParser ?? new Ts4BondHeaderParser();
    }

    public async Task<Ts4SimAppearanceSnapshot> BuildSnapshotAsync(
        string savePath,
        string gameRoot,
        string modsRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);

        var fullSavePath = Path.GetFullPath(savePath);
        if (!File.Exists(fullSavePath))
        {
            throw new FileNotFoundException("Save file was not found.", fullSavePath);
        }

        var issues = new List<Ts4AppearanceIssue>();
        var stats = new ResourceStatsCounter();
        var saveData = LoadSave(fullSavePath);

        var packageFiles = EnumeratePackageFiles(modsRoot, gameRoot, issues, cancellationToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new DbpfCatalogPackageFile(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
            })
            .OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rootHint = !string.IsNullOrWhiteSpace(modsRoot)
            ? modsRoot
            : !string.IsNullOrWhiteSpace(gameRoot)
                ? gameRoot
                : Path.GetDirectoryName(fullSavePath) ?? Environment.CurrentDirectory;
        var catalogSnapshot = await _catalog.BuildSnapshotAsync(rootHint, packageFiles, cancellationToken: cancellationToken).ConfigureAwait(false);

        var morphSourceKeys = new HashSet<DbpfResourceKey>();
        var casPartCache = new Dictionary<DbpfResourceKey, CasPartParseResult>();
        var sims = new List<Ts4SimAppearanceSim>(saveData.sims.Count);

        foreach (var sim in saveData.sims)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullName = $"{sim.first_name} {sim.last_name}".Trim();
            if (sim.extended_species > 1)
            {
                issues.Add(BuildIssue(
                    IssueCodeSpeciesLimitation,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Sim,
                    $"Species {sim.extended_species} may have incomplete appearance support in this phase.",
                    simId: sim.sim_id));
            }

            var outfits = new List<Ts4OutfitAppearance>();
            var sourceOutfits = sim.outfits?.outfits ?? new List<OutfitData>();
            foreach (var outfit in sourceOutfits)
            {
                var partIds = outfit.parts?.ids ?? Array.Empty<ulong>();
                var bodyTypes = outfit.body_types_list?.body_types ?? Array.Empty<uint>();
                var colorShifts = outfit.part_shifts?.color_shift ?? Array.Empty<ulong>();
                var parts = new List<Ts4OutfitPartAppearance>(partIds.Length);

                for (var partIndex = 0; partIndex < partIds.Length; partIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var requestedKey = new DbpfResourceKey(Sims4ResourceTypeRegistry.CasPart, 0, partIds[partIndex]);
                    var bodyType = partIndex < bodyTypes.Length ? bodyTypes[partIndex] : 0u;
                    var colorShift = partIndex < colorShifts.Length ? colorShifts[partIndex] : 0ul;
                    stats.TotalReferences++;

                    DbpfResourceKey? resolvedKey = null;
                    Ts4CasPartExtended? casPart = null;
                    var textureRefs = Array.Empty<DbpfResourceKey>();
                    var meshRefs = Array.Empty<DbpfResourceKey>();
                    DbpfResourceKey? regionMapRef = null;

                    if (!_locator.TryResolveFirst(catalogSnapshot, requestedKey, out var location, ResourceLookupPolicy.PreferModsSdxGame))
                    {
                        stats.MissingReferences++;
                        issues.Add(BuildIssue(
                            IssueCodeResourceNotFound,
                            Ts4AppearanceIssueSeverity.Warning,
                            Ts4AppearanceIssueScope.Part,
                            $"CASP resource was not found for requested part {FormatKey(requestedKey)}.",
                            requestedKey,
                            sim.sim_id));
                    }
                    else
                    {
                        stats.ResolvedReferences++;
                        resolvedKey = new DbpfResourceKey(location.Entry.Type, location.Entry.Group, location.Entry.Instance);
                        casPart = GetOrParseCasPart(
                            resolvedKey.Value,
                            location,
                            casPartCache,
                            issues,
                            stats,
                            sim.sim_id);
                        if (casPart is not null)
                        {
                            textureRefs = casPart.BaseInfo.TextureRefs.AllDistinct.ToArray();
                            meshRefs = casPart.LodEntries
                                .SelectMany(entry => entry.MeshParts)
                                .Distinct()
                                .ToArray();
                            regionMapRef = casPart.RegionMapRef;
                        }
                    }

                    parts.Add(new Ts4OutfitPartAppearance
                    {
                        RequestedCasPartKey = requestedKey,
                        ResolvedCasPartKey = resolvedKey,
                        BodyType = bodyType,
                        ColorShift = colorShift,
                        CasPart = casPart,
                        TextureRefs = textureRefs,
                        MeshRefs = meshRefs,
                        RegionMapRef = regionMapRef
                    });
                }

                outfits.Add(new Ts4OutfitAppearance
                {
                    OutfitId = outfit.outfit_id,
                    Category = outfit.category,
                    OutfitFlags = outfit.outfit_flags,
                    CreatedTicks = outfit.created,
                    Parts = parts
                });
            }

            var modifierCount = 0;
            modifierCount += ExtractMorphSourceKeys(
                sim.facial_attr,
                morphSourceKeys,
                issues,
                $"sim:{sim.sim_id:X16}:facial_attr",
                sim.sim_id);
            modifierCount += ExtractMorphSourceKeys(
                sim.genetic_data?.sculpts_and_mods_attr,
                morphSourceKeys,
                issues,
                $"sim:{sim.sim_id:X16}:genetic_data.sculpts_and_mods_attr",
                sim.sim_id);

            var requestedSimInfoKey = new DbpfResourceKey(Sims4ResourceTypeRegistry.SimInfo, 0, sim.sim_id);
            var simInfo = default(Ts4SimInfoResource);
            DbpfResourceKey? resolvedSimInfoKey = null;
            stats.TotalReferences++;
            if (_locator.TryResolveFirst(catalogSnapshot, requestedSimInfoKey, out var simInfoLocation, ResourceLookupPolicy.PreferModsSdxGame))
            {
                stats.ResolvedReferences++;
                resolvedSimInfoKey = new DbpfResourceKey(simInfoLocation.Entry.Type, simInfoLocation.Entry.Group, simInfoLocation.Entry.Instance);
                if (TryReadBytes(simInfoLocation, out var simInfoBytes, out var readError))
                {
                    if (!_simInfoParser.TryParse(resolvedSimInfoKey.Value, simInfoBytes, out simInfo, out var parseError))
                    {
                        stats.ParseFailures++;
                        issues.Add(BuildIssue(
                            IssueCodeParserFailed,
                            Ts4AppearanceIssueSeverity.Warning,
                            Ts4AppearanceIssueScope.Sim,
                            parseError ?? $"Failed to parse SIM_INFO {FormatKey(resolvedSimInfoKey.Value)}.",
                            resolvedSimInfoKey,
                            sim.sim_id));
                    }
                }
                else
                {
                    stats.ParseFailures++;
                    issues.Add(BuildIssue(
                        IssueCodeResourceReadFailed,
                        Ts4AppearanceIssueSeverity.Warning,
                        Ts4AppearanceIssueScope.Sim,
                        readError ?? $"Failed to read SIM_INFO {FormatKey(resolvedSimInfoKey.Value)}.",
                        resolvedSimInfoKey,
                        sim.sim_id));
                }
            }
            else
            {
                stats.MissingReferences++;
                issues.Add(BuildIssue(
                    IssueCodeResourceNotFound,
                    Ts4AppearanceIssueSeverity.Info,
                    Ts4AppearanceIssueScope.Sim,
                    $"SIM_INFO resource was not found for sim id 0x{sim.sim_id:X16}.",
                    requestedSimInfoKey,
                    sim.sim_id));
            }

            sims.Add(new Ts4SimAppearanceSim
            {
                SimId = sim.sim_id,
                FullName = fullName,
                Species = sim.extended_species,
                ModifierCount = modifierCount,
                Outfits = outfits,
                SimInfoKey = resolvedSimInfoKey,
                SimInfo = simInfo
            });
        }

        var morphResources = new Dictionary<DbpfResourceKey, byte[]>();
        foreach (var sourceKey in morphSourceKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            stats.TotalReferences++;
            if (!_locator.TryResolveFirst(catalogSnapshot, sourceKey, out var location, ResourceLookupPolicy.PreferModsSdxGame))
            {
                stats.MissingReferences++;
                issues.Add(BuildIssue(
                    IssueCodeResourceNotFound,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Morph,
                    $"Morph source resource was not found: {FormatKey(sourceKey)}.",
                    sourceKey));
                continue;
            }

            stats.ResolvedReferences++;
            var resolvedKey = new DbpfResourceKey(location.Entry.Type, location.Entry.Group, location.Entry.Instance);
            if (TryReadBytes(location, out var bytes, out var error))
            {
                morphResources[resolvedKey] = bytes;
            }
            else
            {
                stats.ParseFailures++;
                issues.Add(BuildIssue(
                    IssueCodeResourceReadFailed,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Morph,
                    error ?? $"Failed to read morph source resource {FormatKey(resolvedKey)}.",
                    resolvedKey));
            }
        }

        var rawMorphGraph = _morphGraphBuilder.Build(morphResources);
        foreach (var graphIssue in rawMorphGraph.Issues)
        {
            issues.Add(BuildIssue(
                IssueCodeMorphGraphParseFailed,
                Ts4AppearanceIssueSeverity.Warning,
                Ts4AppearanceIssueScope.Morph,
                graphIssue));
        }

        var referencedResources = BuildMorphReferencedResourceHealth(
            catalogSnapshot,
            rawMorphGraph,
            issues,
            stats,
            cancellationToken);

        var morphGraph = new Ts4MorphLinkGraph
        {
            SimModifierLinks = rawMorphGraph.SimModifierLinks,
            SculptLinks = rawMorphGraph.SculptLinks,
            Issues = rawMorphGraph.Issues,
            ReferencedResources = referencedResources
        };

        return new Ts4SimAppearanceSnapshot
        {
            SavePath = fullSavePath,
            LastWriteTimeLocal = File.GetLastWriteTime(fullSavePath),
            Sims = sims.OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase).ToArray(),
            MorphGraphSummary = morphGraph,
            ResourceStats = new Ts4AppearanceResourceStats
            {
                TotalReferences = stats.TotalReferences,
                ResolvedReferences = stats.ResolvedReferences,
                MissingReferences = stats.MissingReferences,
                ParseFailures = stats.ParseFailures
            },
            Issues = issues
        };
    }

    private SaveGameData LoadSave(string savePath)
    {
        var package = DbpfPackageIndexReader.ReadPackageIndex(savePath);
        var entry = package.Entries.FirstOrDefault(candidate => !candidate.IsDeleted && candidate.Type == SaveDataResourceType);
        if (entry.Type != SaveDataResourceType)
        {
            throw new InvalidDataException("SaveGameData resource (0x0000000D) was not found.");
        }

        using var session = _resourceReader.OpenSession(savePath);
        if (!session.TryReadBytes(entry, out var bytes, out var error))
        {
            throw new InvalidDataException(error ?? "Failed to read save resource.");
        }

        using var stream = new MemoryStream(bytes, writable: false);
        return Serializer.Deserialize<SaveGameData>(stream);
    }

    private Ts4CasPartExtended? GetOrParseCasPart(
        DbpfResourceKey resolvedKey,
        ResourceLocation location,
        IDictionary<DbpfResourceKey, CasPartParseResult> cache,
        ICollection<Ts4AppearanceIssue> issues,
        ResourceStatsCounter stats,
        ulong simId)
    {
        if (cache.TryGetValue(resolvedKey, out var cached))
        {
            return cached.CasPart;
        }

        if (!TryReadBytes(location, out var bytes, out var error))
        {
            stats.ParseFailures++;
            issues.Add(BuildIssue(
                IssueCodeResourceReadFailed,
                Ts4AppearanceIssueSeverity.Warning,
                Ts4AppearanceIssueScope.Part,
                error ?? $"Failed to read CASP resource {FormatKey(resolvedKey)}.",
                resolvedKey,
                simId));
            cache[resolvedKey] = new CasPartParseResult(false, null);
            return null;
        }

        if (!Sims4CasPartExtendedParser.TryParse(resolvedKey, bytes, out var casPart, out var parseError))
        {
            stats.ParseFailures++;
            issues.Add(BuildIssue(
                IssueCodeParserFailed,
                Ts4AppearanceIssueSeverity.Warning,
                Ts4AppearanceIssueScope.Part,
                parseError ?? $"Failed to parse CASP {FormatKey(resolvedKey)}.",
                resolvedKey,
                simId));
            cache[resolvedKey] = new CasPartParseResult(false, null);
            return null;
        }

        cache[resolvedKey] = new CasPartParseResult(true, casPart);
        return casPart;
    }

    private IReadOnlyList<Ts4MorphReferencedResourceHealth> BuildMorphReferencedResourceHealth(
        DbpfCatalogSnapshot snapshot,
        Ts4MorphLinkGraph graph,
        ICollection<Ts4AppearanceIssue> issues,
        ResourceStatsCounter stats,
        CancellationToken cancellationToken)
    {
        var morphRefs = new HashSet<DbpfResourceKey>();
        CollectMorphRefs(graph.SimModifierLinks.Values, morphRefs);
        CollectMorphRefs(graph.SculptLinks.Values, morphRefs);

        var health = new List<Ts4MorphReferencedResourceHealth>(morphRefs.Count);
        foreach (var key in morphRefs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = ResolveMorphReferencedResourceKind(key.Type);
            stats.TotalReferences++;

            if (!_locator.TryResolveFirst(snapshot, key, out var location, ResourceLookupPolicy.PreferModsSdxGame))
            {
                stats.MissingReferences++;
                issues.Add(BuildIssue(
                    IssueCodeResourceNotFound,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Morph,
                    $"Referenced morph resource was not found: {FormatKey(key)}.",
                    key));
                health.Add(new Ts4MorphReferencedResourceHealth
                {
                    Key = key,
                    Kind = kind,
                    Exists = false,
                    HeaderParsed = false,
                    HeaderSummary = string.Empty,
                    Error = "Resource not found."
                });
                continue;
            }

            stats.ResolvedReferences++;
            var resolvedKey = new DbpfResourceKey(location.Entry.Type, location.Entry.Group, location.Entry.Instance);
            if (!TryReadBytes(location, out var bytes, out var readError))
            {
                stats.ParseFailures++;
                issues.Add(BuildIssue(
                    IssueCodeResourceReadFailed,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Morph,
                    readError ?? $"Failed to read morph resource {FormatKey(resolvedKey)}.",
                    resolvedKey));
                health.Add(new Ts4MorphReferencedResourceHealth
                {
                    Key = resolvedKey,
                    Kind = kind,
                    Exists = true,
                    HeaderParsed = false,
                    HeaderSummary = string.Empty,
                    Error = readError ?? "Failed to read resource payload."
                });
                continue;
            }

            var parsed = TryParseMorphHeader(kind, resolvedKey, bytes, out var headerSummary, out var parseError);
            if (!parsed)
            {
                stats.ParseFailures++;
                issues.Add(BuildIssue(
                    IssueCodeParserFailed,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Morph,
                    parseError ?? $"Failed to parse morph resource header {FormatKey(resolvedKey)}.",
                    resolvedKey));
            }

            health.Add(new Ts4MorphReferencedResourceHealth
            {
                Key = resolvedKey,
                Kind = kind,
                Exists = true,
                HeaderParsed = parsed,
                HeaderSummary = headerSummary,
                Error = parsed ? string.Empty : parseError ?? "Header parse failed."
            });
        }

        return health;
    }

    private static void CollectMorphRefs(IEnumerable<Ts4MorphReference> refs, ISet<DbpfResourceKey> keys)
    {
        foreach (var morphRef in refs)
        {
            foreach (var bgeoRef in morphRef.BgeoRefs)
            {
                if (bgeoRef.Instance != 0)
                {
                    keys.Add(bgeoRef);
                }
            }

            if (morphRef.DmapShapeRef is { Instance: not 0 } shapeRef)
            {
                keys.Add(shapeRef);
            }

            if (morphRef.DmapNormalRef is { Instance: not 0 } normalRef)
            {
                keys.Add(normalRef);
            }

            if (morphRef.BoneDeltaRef is { Instance: not 0 } boneRef)
            {
                keys.Add(boneRef);
            }
        }
    }

    private bool TryParseMorphHeader(
        Ts4MorphReferencedResourceKind kind,
        DbpfResourceKey key,
        byte[] bytes,
        out string summary,
        out string? error)
    {
        summary = string.Empty;
        error = null;

        switch (kind)
        {
            case Ts4MorphReferencedResourceKind.BlendGeometry:
                if (_bgeoHeaderParser.TryParse(key, bytes, out var bgeoHeader, out error))
                {
                    summary = $"ctx={bgeoHeader.ContextVersion},ver={bgeoHeader.Version},lod={bgeoHeader.LodCount},verts={bgeoHeader.TotalVertexCount},vectors={bgeoHeader.TotalVectorCount}";
                    return true;
                }

                return false;
            case Ts4MorphReferencedResourceKind.DeformerMap:
                if (_dmapHeaderParser.TryParse(key, bytes, out var dmapHeader, out error))
                {
                    summary = $"ver={dmapHeader.Version},{dmapHeader.Width}x{dmapHeader.Height},species={dmapHeader.Species},ageGender={dmapHeader.AgeGender},physique={dmapHeader.Physique},shapeOrNormals={dmapHeader.ShapeOrNormals},robe={dmapHeader.HasRobeChannel}";
                    return true;
                }

                return false;
            case Ts4MorphReferencedResourceKind.BoneDelta:
                if (_bondHeaderParser.TryParse(key, bytes, out var bondHeader, out error))
                {
                    summary = $"ctx={bondHeader.ContextVersion},ver={bondHeader.Version},adjustments={bondHeader.BoneAdjustCount}";
                    return true;
                }

                return false;
            default:
                error = "Unsupported morph resource type.";
                return false;
        }
    }

    private static Ts4MorphReferencedResourceKind ResolveMorphReferencedResourceKind(uint type)
    {
        if (type == Sims4ResourceTypeRegistry.BlendGeometry)
        {
            return Ts4MorphReferencedResourceKind.BlendGeometry;
        }

        if (type == Sims4ResourceTypeRegistry.DeformerMap)
        {
            return Ts4MorphReferencedResourceKind.DeformerMap;
        }

        if (type == Sims4ResourceTypeRegistry.BoneDelta)
        {
            return Ts4MorphReferencedResourceKind.BoneDelta;
        }

        return Ts4MorphReferencedResourceKind.Unknown;
    }

    private bool TryReadBytes(ResourceLocation location, out byte[] bytes, out string? error)
    {
        using var session = _resourceReader.OpenSession(location.FilePath);
        return session.TryReadBytes(location.Entry, out bytes, out error);
    }

    private static int ExtractMorphSourceKeys(
        byte[]? payload,
        ISet<DbpfResourceKey> keys,
        ICollection<Ts4AppearanceIssue> issues,
        string sourceLabel,
        ulong simId)
    {
        if (payload is null || payload.Length == 0)
        {
            return 0;
        }

        BlobSimFacialCustomizationData blob;
        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            blob = Serializer.Deserialize<BlobSimFacialCustomizationData>(stream);
        }
        catch (Exception ex)
        {
            issues.Add(BuildIssue(
                IssueCodeMorphPayloadInvalid,
                Ts4AppearanceIssueSeverity.Warning,
                Ts4AppearanceIssueScope.Morph,
                $"Failed to parse morph payload ({sourceLabel}): {ex.Message}",
                simId: simId));
            return 0;
        }

        var count = 0;
        if (blob.sculpts is not null)
        {
            foreach (var sculpt in blob.sculpts)
            {
                if (sculpt == 0)
                {
                    continue;
                }

                keys.Add(new DbpfResourceKey(Sims4ResourceTypeRegistry.Sculpt, 0, sculpt));
            }

            count += blob.sculpts.Length;
        }

        if (blob.face_modifiers is not null)
        {
            foreach (var modifier in blob.face_modifiers)
            {
                if (modifier.key == 0)
                {
                    continue;
                }

                keys.Add(new DbpfResourceKey(Sims4ResourceTypeRegistry.SimModifier, 0, modifier.key));
                count++;
            }
        }

        if (blob.body_modifiers is not null)
        {
            foreach (var modifier in blob.body_modifiers)
            {
                if (modifier.key == 0)
                {
                    continue;
                }

                keys.Add(new DbpfResourceKey(Sims4ResourceTypeRegistry.SimModifier, 0, modifier.key));
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<string> EnumeratePackageFiles(
        string modsRoot,
        string gameRoot,
        ICollection<Ts4AppearanceIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (var path in EnumeratePackageFilesUnderRoot(modsRoot, "modsRoot", issues, cancellationToken))
        {
            yield return path;
        }

        foreach (var path in EnumeratePackageFilesUnderRoot(gameRoot, "gameRoot", issues, cancellationToken))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> EnumeratePackageFilesUnderRoot(
        string rootPath,
        string rootLabel,
        ICollection<Ts4AppearanceIssue> issues,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = pending.Pop();
            string[] childDirectories;
            string[] packageFiles;

            try
            {
                childDirectories = Directory.GetDirectories(current);
                packageFiles = Directory.GetFiles(current, "*.package");
            }
            catch (Exception ex)
            {
                issues.Add(BuildIssue(
                    IssueCodePackageEnumerationFailed,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Snapshot,
                    $"Failed to enumerate packages under {rootLabel} ({current}): {ex.Message}"));
                continue;
            }

            foreach (var child in childDirectories)
            {
                if (IsReparseDirectory(child))
                {
                    continue;
                }

                pending.Push(child);
            }

            foreach (var file in packageFiles)
            {
                yield return file;
            }
        }
    }

    private static bool IsReparseDirectory(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch
        {
            return false;
        }
    }

    private static Ts4AppearanceIssue BuildIssue(
        string code,
        Ts4AppearanceIssueSeverity severity,
        Ts4AppearanceIssueScope scope,
        string message,
        DbpfResourceKey? resourceKey = null,
        ulong? simId = null)
    {
        return new Ts4AppearanceIssue
        {
            Code = code,
            Severity = severity,
            Scope = scope,
            Message = message,
            ResourceKey = resourceKey,
            SimId = simId
        };
    }

    private static string FormatKey(DbpfResourceKey key)
    {
        return $"{key.Type:X8}:{key.Group:X8}:{key.Instance:X16}";
    }

    private sealed class CasPartParseResult
    {
        public CasPartParseResult(bool success, Ts4CasPartExtended? casPart)
        {
            Success = success;
            CasPart = casPart;
        }

        public bool Success { get; }
        public Ts4CasPartExtended? CasPart { get; }
    }

    private sealed class ResourceStatsCounter
    {
        public int TotalReferences { get; set; }
        public int ResolvedReferences { get; set; }
        public int MissingReferences { get; set; }
        public int ParseFailures { get; set; }
    }
}
