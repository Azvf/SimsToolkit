using EA.Sims4.Persistence;
using ProtoBuf;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.SaveData.Services;

public sealed class SaveAppearanceLinkService : ISaveAppearanceLinkService, ITS4SimAppearanceService
{
    private const uint SaveDataResourceType = 0x0000000D;
    private const string CasModifierTuningPackageName = "CASModifierTuning.package";
    private const string IssueCodeResourceNotFound = "RESOURCE_NOT_FOUND";
    private const string IssueCodeResourceReadFailed = "RESOURCE_READ_FAILED";
    private const string IssueCodeParserFailed = "PARSER_FAILED";
    private const string IssueCodeMorphPayloadInvalid = "MORPH_PAYLOAD_INVALID";
    private const string IssueCodeMorphGraphParseFailed = "MORPH_GRAPH_PARSE_FAILED";
    private const string IssueCodePackageEnumerationFailed = "PACKAGE_ENUMERATION_FAILED";
    private const string IssueCodeSpeciesLimitation = "SPECIES_LIMITATION";
    private const string IssueCodeCasModifierTuningMissing = "CASMOD_TUNING_MISSING";
    private const string IssueCodeCasModifierTuningParseFailed = "CASMOD_TUNING_PARSE_FAILED";
    private const string IssueCodeRegionMapParseFailed = "REGIONMAP_PARSE_FAILED";
    private const string IssueCodeToneParseFailed = "TONE_PARSE_FAILED";
    private const string IssueCodePeltLayerParseFailed = "PELTLAYER_PARSE_FAILED";
    private const string IssueCodeRigParseFailed = "RIG_PARSE_FAILED";
    private const string IssueCodeBondBoneHashUnmapped = "BOND_BONE_HASH_UNMAPPED";
    private const string IssueCodeGeomParseFailed = "GEOM_PARSE_FAILED";
    private const string IssueCodeTextureParseFailed = "TEXTURE_PARSE_FAILED";
    private const string IssueCodeTextureDecodeFailed = "TEXTURE_DECODE_FAILED";

    private readonly IDbpfPackageCatalog _catalog;
    private readonly IDbpfResourceReader _resourceReader;
    private readonly ITS4ResourceLocator _locator;
    private readonly ITS4ResourceParser<Ts4SimInfoResource> _simInfoParser;
    private readonly Ts4MorphLinkGraphBuilder _morphGraphBuilder;
    private readonly ITS4ResourceParser<Ts4BgeoHeader> _bgeoHeaderParser;
    private readonly ITS4ResourceParser<Ts4DmapHeader> _dmapHeaderParser;
    private readonly ITS4ResourceParser<Ts4Bond> _bondParser;
    private readonly ITS4ResourceParser<Ts4RegionMap> _regionMapParser;
    private readonly ITS4ResourceParser<Ts4Tone> _toneParser;
    private readonly ITS4ResourceParser<Ts4PeltLayer> _peltLayerParser;
    private readonly ITS4ResourceParser<Ts4Rig> _rigParser;
    private readonly ITS4ResourceParser<Ts4Geom> _geomParser;
    private readonly ITS4ResourceParser<Ts4TextureReadMetadata> _textureMetadataParser;
    private readonly Ts4CasModifierTuningCatalogLoader _casModifierCatalogLoader;

    public SaveAppearanceLinkService(
        IDbpfPackageCatalog? catalog = null,
        IDbpfResourceReader? resourceReader = null,
        ITS4ResourceLocator? locator = null,
        ITS4ResourceParser<Ts4SimInfoResource>? simInfoParser = null,
        Ts4MorphLinkGraphBuilder? morphGraphBuilder = null,
        ITS4ResourceParser<Ts4BgeoHeader>? bgeoHeaderParser = null,
        ITS4ResourceParser<Ts4DmapHeader>? dmapHeaderParser = null,
        ITS4ResourceParser<Ts4Bond>? bondParser = null,
        ITS4ResourceParser<Ts4RegionMap>? regionMapParser = null,
        ITS4ResourceParser<Ts4Tone>? toneParser = null,
        ITS4ResourceParser<Ts4PeltLayer>? peltLayerParser = null,
        ITS4ResourceParser<Ts4Rig>? rigParser = null,
        ITS4ResourceParser<Ts4Geom>? geomParser = null,
        ITS4ResourceParser<Ts4TextureReadMetadata>? textureMetadataParser = null,
        Ts4CasModifierTuningCatalogLoader? casModifierCatalogLoader = null)
    {
        _catalog = catalog ?? new DbpfPackageCatalog();
        _resourceReader = resourceReader ?? new DbpfResourceReader();
        _locator = locator ?? new Ts4ResourceLocator();
        _simInfoParser = simInfoParser ?? new Ts4SimInfoResourceParser();
        _morphGraphBuilder = morphGraphBuilder ?? new Ts4MorphLinkGraphBuilder();
        _bgeoHeaderParser = bgeoHeaderParser ?? new Ts4BgeoHeaderParser();
        _dmapHeaderParser = dmapHeaderParser ?? new Ts4DmapHeaderParser();
        _bondParser = bondParser ?? new Ts4BondParser();
        _regionMapParser = regionMapParser ?? new Ts4RegionMapParser();
        _toneParser = toneParser ?? new Ts4ToneParser();
        _peltLayerParser = peltLayerParser ?? new Ts4PeltLayerParser();
        _rigParser = rigParser ?? new Ts4RigParser();
        _geomParser = geomParser ?? new Ts4GeomParser();
        _textureMetadataParser = textureMetadataParser ?? new Ts4TextureMetadataParser();
        _casModifierCatalogLoader = casModifierCatalogLoader ?? new Ts4CasModifierTuningCatalogLoader(_resourceReader);
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

        var modifierCatalog = LoadModifierCatalog(issues);
        var rigBoneIndex = BuildRigBoneIndex(catalogSnapshot, issues, cancellationToken);

        var morphSourceKeys = new HashSet<DbpfResourceKey>();
        var casPartCache = new Dictionary<DbpfResourceKey, CasPartParseResult>();
        var regionMapCache = new Dictionary<DbpfResourceKey, ResourceParseResult<Ts4RegionMap>>();
        var toneCache = new Dictionary<DbpfResourceKey, ResourceParseResult<Ts4Tone>>();
        var peltLayerCache = new Dictionary<DbpfResourceKey, ResourceParseResult<Ts4PeltLayer>>();
        var geomCache = new Dictionary<DbpfResourceKey, ResourceParseResult<Ts4Geom>>();
        var textureMetadataCache = new Dictionary<DbpfResourceKey, ResourceParseResult<Ts4TextureReadMetadata>>();
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
                    Ts4ResourceResolution? casPartResolution = null;
                    Ts4CasPartExtended? casPart = null;
                    var textureRefs = Array.Empty<DbpfResourceKey>();
                    var meshRefs = Array.Empty<DbpfResourceKey>();
                    IReadOnlyList<Ts4OutfitMeshAppearance> meshes = Array.Empty<Ts4OutfitMeshAppearance>();
                    IReadOnlyList<Ts4OutfitTextureAppearance> textures = Array.Empty<Ts4OutfitTextureAppearance>();
                    DbpfResourceKey? regionMapRef = null;
                    DbpfResourceKey? resolvedRegionMapRef = null;
                    Ts4ResourceResolution? regionMapResolution = null;
                    Ts4RegionMap? regionMap = null;

                    casPartResolution = _locator.Resolve(catalogSnapshot, requestedKey, ResourceLookupPolicy.PreferModsSdxGame);
                    if (casPartResolution.SelectedLocation is not { } location)
                    {
                        stats.MissingReferences++;
                        issues.Add(BuildIssue(
                            IssueCodeResourceNotFound,
                            Ts4AppearanceIssueSeverity.Warning,
                            Ts4AppearanceIssueScope.Part,
                            $"CASP resource was not found for requested part {FormatKey(requestedKey)}.",
                            requestedKey,
                            sim.sim_id,
                            casPartResolution));
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
                            sim.sim_id,
                            casPartResolution);
                        if (casPart is not null)
                        {
                            textureRefs = casPart.BaseInfo.TextureRefs.AllDistinct.ToArray();
                            meshRefs = casPart.LodEntries
                                .SelectMany(entry => entry.MeshParts)
                                .Distinct()
                                .ToArray();
                            meshes = BuildMeshAppearances(
                                catalogSnapshot,
                                meshRefs,
                                geomCache,
                                issues,
                                stats,
                                sim.sim_id);
                            textures = BuildTextureAppearances(
                                catalogSnapshot,
                                resolvedKey.Value,
                                casPart,
                                textureMetadataCache,
                                issues,
                                stats,
                                sim.sim_id);
                            regionMapRef = casPart.RegionMapRef;

                            if (regionMapRef is { Instance: not 0 } requestedRegionMapKey)
                            {
                                stats.TotalReferences++;
                                regionMapResolution = _locator.Resolve(catalogSnapshot, requestedRegionMapKey, ResourceLookupPolicy.PreferModsSdxGame);
                                if (regionMapResolution.SelectedLocation is { } regionMapLocation)
                                {
                                    stats.ResolvedReferences++;
                                    resolvedRegionMapRef = new DbpfResourceKey(regionMapLocation.Entry.Type, regionMapLocation.Entry.Group, regionMapLocation.Entry.Instance);
                                    regionMap = GetOrParseResource(
                                        resolvedRegionMapRef.Value,
                                        regionMapLocation,
                                        regionMapCache,
                                        _regionMapParser,
                                        IssueCodeRegionMapParseFailed,
                                        Ts4AppearanceIssueScope.Part,
                                        "RMAP",
                                        issues,
                                        stats,
                                        sim.sim_id,
                                        regionMapResolution);
                                }
                                else
                                {
                                    stats.MissingReferences++;
                                    issues.Add(BuildIssue(
                                        IssueCodeResourceNotFound,
                                        Ts4AppearanceIssueSeverity.Warning,
                                        Ts4AppearanceIssueScope.Part,
                                        $"RegionMap resource was not found for CASP {FormatKey(resolvedKey.Value)}.",
                                        requestedRegionMapKey,
                                        sim.sim_id,
                                        regionMapResolution));
                                }
                            }
                        }
                    }

                    parts.Add(new Ts4OutfitPartAppearance
                    {
                        RequestedCasPartKey = requestedKey,
                        ResolvedCasPartKey = resolvedKey,
                        CasPartResolution = casPartResolution,
                        BodyType = bodyType,
                        ColorShift = colorShift,
                        CasPart = casPart,
                        TextureRefs = textureRefs,
                        MeshRefs = meshRefs,
                        Meshes = meshes,
                        Textures = textures,
                        RegionMapRef = regionMapRef,
                        ResolvedRegionMapKey = resolvedRegionMapRef,
                        RegionMapResolution = regionMapResolution,
                        RegionMap = regionMap
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

            var allModifierWeights = new List<ModifierWeightRecord>();
            var modifierCount = 0;
            var facialExtraction = ExtractMorphPayloadData(
                sim.facial_attr,
                morphSourceKeys,
                issues,
                $"sim:{sim.sim_id:X16}:facial_attr",
                sim.sim_id);
            modifierCount += facialExtraction.ModifierCount;
            allModifierWeights.AddRange(facialExtraction.ModifierWeights);

            var geneticExtraction = ExtractMorphPayloadData(
                sim.genetic_data?.sculpts_and_mods_attr,
                morphSourceKeys,
                issues,
                $"sim:{sim.sim_id:X16}:genetic_data.sculpts_and_mods_attr",
                sim.sim_id);
            modifierCount += geneticExtraction.ModifierCount;
            allModifierWeights.AddRange(geneticExtraction.ModifierWeights);

            var requestedSimInfoKey = new DbpfResourceKey(Sims4ResourceTypeRegistry.SimInfo, 0, sim.sim_id);
            var simInfo = default(Ts4SimInfoResource);
            DbpfResourceKey? resolvedSimInfoKey = null;
            Ts4ResourceResolution? simInfoResolution = null;
            stats.TotalReferences++;
            simInfoResolution = _locator.Resolve(catalogSnapshot, requestedSimInfoKey, ResourceLookupPolicy.PreferModsSdxGame);
            if (simInfoResolution.SelectedLocation is { } simInfoLocation)
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
                            sim.sim_id,
                            simInfoResolution));
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
                        sim.sim_id,
                        simInfoResolution));
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
                    sim.sim_id,
                    simInfoResolution));
            }

            DbpfResourceKey? resolvedToneKey = null;
            Ts4ResourceResolution? toneResolution = null;
            Ts4Tone? tone = null;
            var toneInstance = simInfo?.SkinToneRef is > 0 ? simInfo.SkinToneRef : sim.skin_tone;
            if (toneInstance != 0)
            {
                var requestedToneKey = new DbpfResourceKey(Sims4ResourceTypeRegistry.Tone, 0, toneInstance);
                stats.TotalReferences++;
                toneResolution = _locator.Resolve(catalogSnapshot, requestedToneKey, ResourceLookupPolicy.PreferModsSdxGame);
                if (toneResolution.SelectedLocation is { } toneLocation)
                {
                    stats.ResolvedReferences++;
                    resolvedToneKey = new DbpfResourceKey(toneLocation.Entry.Type, toneLocation.Entry.Group, toneLocation.Entry.Instance);
                    tone = GetOrParseResource(
                        resolvedToneKey.Value,
                        toneLocation,
                        toneCache,
                        _toneParser,
                        IssueCodeToneParseFailed,
                        Ts4AppearanceIssueScope.Sim,
                        "TONE",
                        issues,
                        stats,
                        sim.sim_id,
                        toneResolution);
                }
                else
                {
                    stats.MissingReferences++;
                    issues.Add(BuildIssue(
                        IssueCodeResourceNotFound,
                        Ts4AppearanceIssueSeverity.Warning,
                        Ts4AppearanceIssueScope.Sim,
                        $"TONE resource was not found for sim 0x{sim.sim_id:X16}.",
                        requestedToneKey,
                        sim.sim_id,
                        toneResolution));
                }
            }

            var peltLayers = new List<Ts4SimPeltLayerAppearance>();
            foreach (var peltLayerData in sim.pelt_layers?.layers ?? Enumerable.Empty<PeltLayerData>())
            {
                if (peltLayerData.layer_id == 0)
                {
                    continue;
                }

                var requestedPeltLayerKey = new DbpfResourceKey(Sims4ResourceTypeRegistry.PeltLayer, 0, peltLayerData.layer_id);
                stats.TotalReferences++;
                var peltResolution = _locator.Resolve(catalogSnapshot, requestedPeltLayerKey, ResourceLookupPolicy.PreferModsSdxGame);
                if (peltResolution.SelectedLocation is { } peltLocation)
                {
                    stats.ResolvedReferences++;
                    var resolvedPeltLayerKey = new DbpfResourceKey(peltLocation.Entry.Type, peltLocation.Entry.Group, peltLocation.Entry.Instance);
                    var peltLayer = GetOrParseResource(
                        resolvedPeltLayerKey,
                        peltLocation,
                        peltLayerCache,
                        _peltLayerParser,
                        IssueCodePeltLayerParseFailed,
                        Ts4AppearanceIssueScope.Sim,
                        "PELT_LAYER",
                        issues,
                        stats,
                        sim.sim_id,
                        peltResolution);

                    peltLayers.Add(new Ts4SimPeltLayerAppearance
                    {
                        LayerId = peltLayerData.layer_id,
                        Color = peltLayerData.color,
                        ResolvedPeltLayerKey = resolvedPeltLayerKey,
                        PeltLayerResolution = peltResolution,
                        PeltLayer = peltLayer
                    });
                }
                else
                {
                    stats.MissingReferences++;
                    issues.Add(BuildIssue(
                        IssueCodeResourceNotFound,
                        Ts4AppearanceIssueSeverity.Warning,
                        Ts4AppearanceIssueScope.Sim,
                        $"PELT_LAYER resource was not found for layer 0x{peltLayerData.layer_id:X16}.",
                        requestedPeltLayerKey,
                        sim.sim_id,
                        peltResolution));
                    peltLayers.Add(new Ts4SimPeltLayerAppearance
                    {
                        LayerId = peltLayerData.layer_id,
                        Color = peltLayerData.color,
                        PeltLayerResolution = peltResolution
                    });
                }
            }

            var modifierSemantics = BuildModifierSemantics(allModifierWeights, modifierCatalog);

            sims.Add(new Ts4SimAppearanceSim
            {
                SimId = sim.sim_id,
                FullName = fullName,
                Species = sim.extended_species,
                ModifierCount = modifierCount,
                Outfits = outfits,
                SimInfoKey = resolvedSimInfoKey,
                SimInfo = simInfo,
                SimInfoResolution = simInfoResolution,
                ModifierSemantics = modifierSemantics,
                ToneRef = resolvedToneKey,
                Tone = tone,
                ToneResolution = toneResolution,
                PeltLayers = peltLayers
            });
        }

        var morphResources = new Dictionary<DbpfResourceKey, byte[]>();
        foreach (var sourceKey in morphSourceKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            stats.TotalReferences++;
            var sourceResolution = _locator.Resolve(catalogSnapshot, sourceKey, ResourceLookupPolicy.PreferModsSdxGame);
            if (sourceResolution.SelectedLocation is not { } location)
            {
                stats.MissingReferences++;
                issues.Add(BuildIssue(
                    IssueCodeResourceNotFound,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Morph,
                    $"Morph source resource was not found: {FormatKey(sourceKey)}.",
                    sourceKey,
                    resolution: sourceResolution));
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
                    resolvedKey,
                    resolution: sourceResolution));
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
            rigBoneIndex,
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
            Issues = issues,
            ModifierTuningCatalog = modifierCatalog,
            RigBoneIndexSummary = new Ts4RigBoneIndexSummary
            {
                ParsedRigCount = rigBoneIndex.ParsedRigCount,
                BoneHashCount = rigBoneIndex.BoneNamesByHash.Count,
                DuplicateHashCount = rigBoneIndex.DuplicateHashCount
            }
        };
    }

    private Ts4CasModifierTuningCatalog? LoadModifierCatalog(ICollection<Ts4AppearanceIssue> issues)
    {
        var packagePath = ResolveCasModifierTuningPackagePath();
        if (packagePath is null)
        {
            issues.Add(BuildIssue(
                IssueCodeCasModifierTuningMissing,
                Ts4AppearanceIssueSeverity.Warning,
                Ts4AppearanceIssueScope.Snapshot,
                $"Embedded {CasModifierTuningPackageName} was not found in the application output directory."));
            return null;
        }

        if (!_casModifierCatalogLoader.TryLoadFromPackage(packagePath, out var catalog, out var error))
        {
            issues.Add(BuildIssue(
                IssueCodeCasModifierTuningParseFailed,
                Ts4AppearanceIssueSeverity.Warning,
                Ts4AppearanceIssueScope.Snapshot,
                error ?? $"Failed to load {CasModifierTuningPackageName}."));
            return null;
        }

        if (catalog.LoadIssues.Count > 0)
        {
            issues.Add(BuildIssue(
                IssueCodeCasModifierTuningParseFailed,
                Ts4AppearanceIssueSeverity.Info,
                Ts4AppearanceIssueScope.Snapshot,
                $"CASModifierTuning loaded with {catalog.LoadIssues.Count} parsing warnings."));
        }

        return catalog;
    }

    private static string? ResolveCasModifierTuningPackagePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, CasModifierTuningPackageName),
            Path.Combine(AppContext.BaseDirectory, "Resources", CasModifierTuningPackageName)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private RigBoneIndex BuildRigBoneIndex(
        DbpfCatalogSnapshot snapshot,
        ICollection<Ts4AppearanceIssue> issues,
        CancellationToken cancellationToken)
    {
        var index = new RigBoneIndex();
        var rigInstances = snapshot.TypeInstanceIndex.Keys
            .Where(static candidate => candidate.Type == Sims4ResourceTypeRegistry.Rig)
            .Select(static candidate => candidate.Instance)
            .Distinct()
            .ToArray();

        foreach (var rigInstance in rigInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requestedRigKey = new DbpfResourceKey(Sims4ResourceTypeRegistry.Rig, 0, rigInstance);
            var rigResolution = _locator.Resolve(snapshot, requestedRigKey, ResourceLookupPolicy.PreferModsSdxGame);
            if (rigResolution.SelectedLocation is not { } location)
            {
                continue;
            }

            var resolvedRigKey = new DbpfResourceKey(location.Entry.Type, location.Entry.Group, location.Entry.Instance);
            if (!TryReadBytes(location, out var bytes, out var readError))
            {
                issues.Add(BuildIssue(
                    IssueCodeResourceReadFailed,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Morph,
                    readError ?? $"Failed to read RIG resource {FormatKey(resolvedRigKey)}.",
                    resolvedRigKey,
                    resolution: rigResolution));
                continue;
            }

            if (!_rigParser.TryParse(resolvedRigKey, bytes, out var rig, out var parseError))
            {
                issues.Add(BuildIssue(
                    IssueCodeRigParseFailed,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Morph,
                    parseError ?? $"Failed to parse RIG {FormatKey(resolvedRigKey)}.",
                    resolvedRigKey,
                    resolution: rigResolution));
                continue;
            }

            index.ParsedRigCount++;
            foreach (var bone in rig.Bones)
            {
                if (!index.BoneNamesByHash.TryGetValue(bone.Hash, out var names))
                {
                    names = new HashSet<string>(StringComparer.Ordinal);
                    index.BoneNamesByHash[bone.Hash] = names;
                }

                if (!names.Add(bone.Name))
                {
                    continue;
                }

                if (names.Count > 1)
                {
                    index.DuplicateHashCount++;
                }
            }
        }

        return index;
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
        ulong simId,
        Ts4ResourceResolution? resolution = null)
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
                simId,
                resolution));
            cache[resolvedKey] = new CasPartParseResult(null);
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
                simId,
                resolution));
            cache[resolvedKey] = new CasPartParseResult(null);
            return null;
        }

        cache[resolvedKey] = new CasPartParseResult(casPart);
        return casPart;
    }

    private IReadOnlyList<Ts4OutfitMeshAppearance> BuildMeshAppearances(
        DbpfCatalogSnapshot snapshot,
        IReadOnlyList<DbpfResourceKey> meshRefs,
        IDictionary<DbpfResourceKey, ResourceParseResult<Ts4Geom>> cache,
        ICollection<Ts4AppearanceIssue> issues,
        ResourceStatsCounter stats,
        ulong simId)
    {
        if (meshRefs.Count == 0)
        {
            return Array.Empty<Ts4OutfitMeshAppearance>();
        }

        var meshes = new List<Ts4OutfitMeshAppearance>(meshRefs.Count);
        foreach (var requestedMeshKey in meshRefs)
        {
            stats.TotalReferences++;
            var resolution = _locator.Resolve(snapshot, requestedMeshKey, ResourceLookupPolicy.PreferModsSdxGame);
            if (resolution.SelectedLocation is not { } location)
            {
                stats.MissingReferences++;
                issues.Add(BuildIssue(
                    IssueCodeResourceNotFound,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Part,
                    $"GEOM resource was not found for mesh ref {FormatKey(requestedMeshKey)}.",
                    requestedMeshKey,
                    simId,
                    resolution));
                meshes.Add(new Ts4OutfitMeshAppearance
                {
                    RequestedMeshKey = requestedMeshKey,
                    Resolution = resolution
                });
                continue;
            }

            stats.ResolvedReferences++;
            var resolvedMeshKey = new DbpfResourceKey(location.Entry.Type, location.Entry.Group, location.Entry.Instance);
            var geom = GetOrParseResource(
                resolvedMeshKey,
                location,
                cache,
                _geomParser,
                IssueCodeGeomParseFailed,
                Ts4AppearanceIssueScope.Part,
                "GEOM",
                issues,
                stats,
                simId,
                resolution);
            meshes.Add(new Ts4OutfitMeshAppearance
            {
                RequestedMeshKey = requestedMeshKey,
                ResolvedMeshKey = resolvedMeshKey,
                Resolution = resolution,
                Geom = geom
            });
        }

        return meshes;
    }

    private IReadOnlyList<Ts4OutfitTextureAppearance> BuildTextureAppearances(
        DbpfCatalogSnapshot snapshot,
        DbpfResourceKey casPartKey,
        Ts4CasPartExtended casPart,
        IDictionary<DbpfResourceKey, ResourceParseResult<Ts4TextureReadMetadata>> cache,
        ICollection<Ts4AppearanceIssue> issues,
        ResourceStatsCounter stats,
        ulong simId)
    {
        var textureSlots = EnumerateTextureSlots(casPart.BaseInfo.TextureRefs).ToArray();
        if (textureSlots.Length == 0)
        {
            return Array.Empty<Ts4OutfitTextureAppearance>();
        }

        var textures = new List<Ts4OutfitTextureAppearance>(textureSlots.Length);
        foreach (var (slot, requestedTextureKey) in textureSlots)
        {
            stats.TotalReferences++;
            var resolution = _locator.Resolve(snapshot, requestedTextureKey, ResourceLookupPolicy.PreferModsSdxGame);
            if (resolution.SelectedLocation is not { } location)
            {
                stats.MissingReferences++;
                issues.Add(BuildIssue(
                    IssueCodeResourceNotFound,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Part,
                    $"Texture ({slot}) was not found for CASP {FormatKey(casPartKey)}.",
                    requestedTextureKey,
                    simId,
                    resolution));
                textures.Add(new Ts4OutfitTextureAppearance
                {
                    Slot = slot,
                    RequestedTextureKey = requestedTextureKey,
                    Resolution = resolution
                });
                continue;
            }

            stats.ResolvedReferences++;
            var resolvedTextureKey = new DbpfResourceKey(location.Entry.Type, location.Entry.Group, location.Entry.Instance);
            var hadCachedResult = cache.ContainsKey(resolvedTextureKey);
            var metadata = GetOrParseResource(
                resolvedTextureKey,
                location,
                cache,
                _textureMetadataParser,
                IssueCodeTextureParseFailed,
                Ts4AppearanceIssueScope.Part,
                "Texture",
                issues,
                stats,
                simId,
                resolution);
            if (!hadCachedResult &&
                metadata is not null &&
                !metadata.Mip0Decoded &&
                !string.IsNullOrWhiteSpace(metadata.DecodeError))
            {
                stats.ParseFailures++;
                issues.Add(BuildIssue(
                    IssueCodeTextureDecodeFailed,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Part,
                    $"Texture ({slot}) decode failed for {FormatKey(resolvedTextureKey)}: {metadata.DecodeError}",
                    resolvedTextureKey,
                    simId,
                    resolution));
            }

            textures.Add(new Ts4OutfitTextureAppearance
            {
                Slot = slot,
                RequestedTextureKey = requestedTextureKey,
                ResolvedTextureKey = resolvedTextureKey,
                Resolution = resolution,
                Metadata = metadata
            });
        }

        return textures;
    }

    private static IEnumerable<(Ts4CasTextureSlot Slot, DbpfResourceKey Key)> EnumerateTextureSlots(Sims4CasPartTextureRefs refs)
    {
        if (refs.Diffuse is { Instance: not 0 } diffuse)
        {
            yield return (Ts4CasTextureSlot.Diffuse, diffuse);
        }

        if (refs.Shadow is { Instance: not 0 } shadow)
        {
            yield return (Ts4CasTextureSlot.Shadow, shadow);
        }

        if (refs.Normal is { Instance: not 0 } normal)
        {
            yield return (Ts4CasTextureSlot.Normal, normal);
        }

        if (refs.Specular is { Instance: not 0 } specular)
        {
            yield return (Ts4CasTextureSlot.Specular, specular);
        }

        if (refs.Emission is { Instance: not 0 } emission)
        {
            yield return (Ts4CasTextureSlot.Emission, emission);
        }
    }

    private T? GetOrParseResource<T>(
        DbpfResourceKey resolvedKey,
        ResourceLocation location,
        IDictionary<DbpfResourceKey, ResourceParseResult<T>> cache,
        ITS4ResourceParser<T> parser,
        string parserIssueCode,
        Ts4AppearanceIssueScope scope,
        string resourceLabel,
        ICollection<Ts4AppearanceIssue> issues,
        ResourceStatsCounter stats,
        ulong? simId = null,
        Ts4ResourceResolution? resolution = null)
        where T : class
    {
        if (cache.TryGetValue(resolvedKey, out var cached))
        {
            return cached.Value;
        }

        if (!TryReadBytes(location, out var bytes, out var readError))
        {
            stats.ParseFailures++;
            issues.Add(BuildIssue(
                IssueCodeResourceReadFailed,
                Ts4AppearanceIssueSeverity.Warning,
                scope,
                readError ?? $"Failed to read {resourceLabel} resource {FormatKey(resolvedKey)}.",
                resolvedKey,
                simId,
                resolution));
            cache[resolvedKey] = new ResourceParseResult<T>(null);
            return null;
        }

        if (!parser.TryParse(resolvedKey, bytes, out var parsed, out var parseError))
        {
            stats.ParseFailures++;
            issues.Add(BuildIssue(
                parserIssueCode,
                Ts4AppearanceIssueSeverity.Warning,
                scope,
                parseError ?? $"Failed to parse {resourceLabel} {FormatKey(resolvedKey)}.",
                resolvedKey,
                simId,
                resolution));
            cache[resolvedKey] = new ResourceParseResult<T>(null);
            return null;
        }

        cache[resolvedKey] = new ResourceParseResult<T>(parsed);
        return parsed;
    }

    private IReadOnlyList<Ts4MorphReferencedResourceHealth> BuildMorphReferencedResourceHealth(
        DbpfCatalogSnapshot snapshot,
        Ts4MorphLinkGraph graph,
        RigBoneIndex rigBoneIndex,
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

            var resolution = _locator.Resolve(snapshot, key, ResourceLookupPolicy.PreferModsSdxGame);
            if (resolution.SelectedLocation is not { } location)
            {
                stats.MissingReferences++;
                issues.Add(BuildIssue(
                    IssueCodeResourceNotFound,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Morph,
                    $"Referenced morph resource was not found: {FormatKey(key)}.",
                    key,
                    resolution: resolution));
                health.Add(new Ts4MorphReferencedResourceHealth
                {
                    Key = key,
                    RequestedKey = key,
                    ResolvedKey = null,
                    Resolution = resolution,
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
                    resolvedKey,
                    resolution: resolution));
                health.Add(new Ts4MorphReferencedResourceHealth
                {
                    Key = resolvedKey,
                    RequestedKey = key,
                    ResolvedKey = resolvedKey,
                    Resolution = resolution,
                    Kind = kind,
                    Exists = true,
                    HeaderParsed = false,
                    HeaderSummary = string.Empty,
                    Error = readError ?? "Failed to read resource payload."
                });
                continue;
            }

            var parsed = false;
            var parseError = default(string);
            var headerSummary = string.Empty;
            var adjustmentRows = Array.Empty<Ts4BondAdjustmentInterpretation>();
            var mappedCount = 0;
            var unmappedCount = 0;

            switch (kind)
            {
                case Ts4MorphReferencedResourceKind.BlendGeometry:
                    if (_bgeoHeaderParser.TryParse(resolvedKey, bytes, out var bgeoHeader, out parseError))
                    {
                        parsed = true;
                        headerSummary = $"ctx={bgeoHeader.ContextVersion},ver={bgeoHeader.Version},lod={bgeoHeader.LodCount},verts={bgeoHeader.TotalVertexCount},vectors={bgeoHeader.TotalVectorCount}";
                    }

                    break;
                case Ts4MorphReferencedResourceKind.DeformerMap:
                    if (_dmapHeaderParser.TryParse(resolvedKey, bytes, out var dmapHeader, out parseError))
                    {
                        parsed = true;
                        headerSummary = $"ver={dmapHeader.Version},{dmapHeader.Width}x{dmapHeader.Height},species={dmapHeader.Species},ageGender={dmapHeader.AgeGender},physique={dmapHeader.Physique},shapeOrNormals={dmapHeader.ShapeOrNormals},robe={dmapHeader.HasRobeChannel}";
                    }

                    break;
                case Ts4MorphReferencedResourceKind.BoneDelta:
                    if (_bondParser.TryParse(resolvedKey, bytes, out var bond, out parseError))
                    {
                        parsed = true;
                        var rows = new List<Ts4BondAdjustmentInterpretation>(bond.Adjustments.Count);
                        foreach (var adjustment in bond.Adjustments)
                        {
                            var nameResolved = rigBoneIndex.TryResolveBoneName(adjustment.SlotHash, out var boneName);
                            if (nameResolved)
                            {
                                mappedCount++;
                            }
                            else
                            {
                                unmappedCount++;
                            }

                            rows.Add(new Ts4BondAdjustmentInterpretation
                            {
                                SlotHash = adjustment.SlotHash,
                                NameResolved = nameResolved,
                                BoneName = boneName ?? string.Empty,
                                OffsetX = adjustment.OffsetX,
                                OffsetY = adjustment.OffsetY,
                                OffsetZ = adjustment.OffsetZ,
                                ScaleX = adjustment.ScaleX,
                                ScaleY = adjustment.ScaleY,
                                ScaleZ = adjustment.ScaleZ
                            });
                        }

                        adjustmentRows = rows.ToArray();
                        headerSummary = $"ctx={bond.ContextVersion},ver={bond.Version},adjustments={bond.Adjustments.Count},mapped={mappedCount},unmapped={unmappedCount}";

                        if (unmappedCount > 0)
                        {
                            issues.Add(BuildIssue(
                                IssueCodeBondBoneHashUnmapped,
                                Ts4AppearanceIssueSeverity.Warning,
                                Ts4AppearanceIssueScope.Morph,
                                $"{unmappedCount} BOND slot hashes were not mapped to any loaded RIG bone name.",
                                resolvedKey,
                                resolution: resolution));
                        }
                    }

                    break;
                default:
                    parseError = "Unsupported morph resource type.";
                    break;
            }

            if (!parsed)
            {
                stats.ParseFailures++;
                issues.Add(BuildIssue(
                    IssueCodeParserFailed,
                    Ts4AppearanceIssueSeverity.Warning,
                    Ts4AppearanceIssueScope.Morph,
                    parseError ?? $"Failed to parse morph resource header {FormatKey(resolvedKey)}.",
                    resolvedKey,
                    resolution: resolution));
            }

            health.Add(new Ts4MorphReferencedResourceHealth
            {
                Key = resolvedKey,
                RequestedKey = key,
                ResolvedKey = resolvedKey,
                Resolution = resolution,
                Kind = kind,
                Exists = true,
                HeaderParsed = parsed,
                HeaderSummary = headerSummary,
                Error = parsed ? string.Empty : parseError ?? "Header parse failed.",
                BondAdjustmentCount = adjustmentRows.Length,
                BondMappedSlotCount = mappedCount,
                BondUnmappedSlotCount = unmappedCount,
                BondAdjustments = adjustmentRows
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

    private static MorphExtractionResult ExtractMorphPayloadData(
        byte[]? payload,
        ISet<DbpfResourceKey> keys,
        ICollection<Ts4AppearanceIssue> issues,
        string sourceLabel,
        ulong simId)
    {
        if (payload is null || payload.Length == 0)
        {
            return MorphExtractionResult.Empty;
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
            return MorphExtractionResult.Empty;
        }

        var modifierWeights = new List<ModifierWeightRecord>();
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
                modifierWeights.Add(new ModifierWeightRecord(modifier.key, modifier.amount, IsFaceModifier: true));
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
                modifierWeights.Add(new ModifierWeightRecord(modifier.key, modifier.amount, IsFaceModifier: false));
                count++;
            }
        }

        return new MorphExtractionResult(count, modifierWeights);
    }

    private static IReadOnlyList<Ts4SimModifierSemanticValue> BuildModifierSemantics(
        IEnumerable<ModifierWeightRecord> modifiers,
        Ts4CasModifierTuningCatalog? catalog)
    {
        var semantics = new List<Ts4SimModifierSemanticValue>();
        foreach (var modifier in modifiers)
        {
            Ts4CasModifierTuningEntry? entry = null;
            if (catalog is not null)
            {
                catalog.ByModifierHash.TryGetValue(modifier.ModifierHash, out entry);
            }

            semantics.Add(new Ts4SimModifierSemanticValue
            {
                ModifierHash = modifier.ModifierHash,
                Weight = modifier.Weight,
                IsFaceModifier = modifier.IsFaceModifier,
                ModifierName = entry?.DisplayName ?? $"0x{modifier.ModifierHash:X16}",
                Scale = entry?.ScaleRules.FirstOrDefault()?.Scale,
                SemanticResolved = entry is not null
            });
        }

        return semantics;
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
        ulong? simId = null,
        Ts4ResourceResolution? resolution = null)
    {
        return new Ts4AppearanceIssue
        {
            Code = code,
            Severity = severity,
            Scope = scope,
            Message = message,
            ResourceKey = resourceKey,
            SimId = simId,
            Resolution = resolution
        };
    }

    private static string FormatKey(DbpfResourceKey key)
    {
        return $"{key.Type:X8}:{key.Group:X8}:{key.Instance:X16}";
    }

    private sealed class CasPartParseResult
    {
        public CasPartParseResult(Ts4CasPartExtended? casPart)
        {
            CasPart = casPart;
        }

        public Ts4CasPartExtended? CasPart { get; }
    }

    private sealed class ResourceParseResult<T>
        where T : class
    {
        public ResourceParseResult(T? value)
        {
            Value = value;
        }

        public T? Value { get; }
    }

    private sealed class RigBoneIndex
    {
        public int ParsedRigCount { get; set; }
        public int DuplicateHashCount { get; set; }
        public IDictionary<uint, HashSet<string>> BoneNamesByHash { get; } = new Dictionary<uint, HashSet<string>>();

        public bool TryResolveBoneName(uint hash, out string? name)
        {
            name = null;
            if (!BoneNamesByHash.TryGetValue(hash, out var names) || names.Count == 0)
            {
                return false;
            }

            name = names.OrderBy(static value => value, StringComparer.Ordinal).First();
            return true;
        }
    }

    private sealed class ResourceStatsCounter
    {
        public int TotalReferences { get; set; }
        public int ResolvedReferences { get; set; }
        public int MissingReferences { get; set; }
        public int ParseFailures { get; set; }
    }

    private readonly record struct ModifierWeightRecord(ulong ModifierHash, float Weight, bool IsFaceModifier);

    private readonly record struct MorphExtractionResult(int ModifierCount, IReadOnlyList<ModifierWeightRecord> ModifierWeights)
    {
        public static MorphExtractionResult Empty { get; } = new(0, Array.Empty<ModifierWeightRecord>());
    }
}
