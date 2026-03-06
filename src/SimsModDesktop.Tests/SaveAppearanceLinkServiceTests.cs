using System.Buffers.Binary;
using System.Text;
using EA.Sims4.Persistence;
using ProtoBuf;
using SimsModDesktop.PackageCore;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Tests;

public sealed class SaveAppearanceLinkServiceTests
{
    [Fact]
    public async Task BuildSnapshotAsync_BuildsOutfitCaspAndMorphSummaries()
    {
        using var fixture = new TempDirectory("save-appearance-full");
        var savePath = Path.Combine(fixture.Path, "slot_00000001.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        const ulong partA = 0x1000000000000001;
        const ulong partB = 0x1000000000000002;
        const ulong smodInstance = 0x5000000000000001;
        const ulong sculptInstance = 0x5000000000000002;
        const ulong bgeoInstance = 0x6000000000000001;
        const ulong dmapShapeInstance = 0x6000000000000002;
        const ulong dmapNormalInstance = 0x6000000000000003;
        const ulong bondInstance = 0x6000000000000004;
        const ulong regionMapPartA = 0x7300000000000001;
        const ulong regionMapPartB = 0x7600000000000001;
        const ulong meshPartA = 0x7200000000000001;
        const ulong meshPartB = 0x7500000000000001;
        const ulong diffusePartA = 0x7100000000000001;
        const ulong normalPartA = 0x7100000000000002;
        const ulong specularPartA = 0x7100000000000003;
        const ulong diffusePartB = 0x7400000000000001;
        const ulong normalPartB = 0x7400000000000002;
        const ulong specularPartB = 0x7400000000000003;
        const ulong toneInstance = 0x8100000000000001;
        const ulong peltLayerInstance = 0x8200000000000001;
        const ulong rigInstance = 0x8300000000000001;

        WritePackageWithSingleResource(
            Path.Combine(modsPath, "part-a.package"),
            Sims4ResourceTypeRegistry.CasPart,
            0xA0000001,
            partA,
            BuildCaspFixture(
                diffuseInstance: diffusePartA,
                normalInstance: normalPartA,
                specularInstance: specularPartA,
                meshInstance: meshPartA,
                regionMapInstance: regionMapPartA));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "part-b.package"),
            Sims4ResourceTypeRegistry.CasPart,
            0xA0000002,
            partB,
            BuildCaspFixture(
                diffuseInstance: diffusePartB,
                normalInstance: normalPartB,
                specularInstance: specularPartB,
                meshInstance: meshPartB,
                regionMapInstance: regionMapPartB));

        WritePackageWithSingleResource(
            Path.Combine(modsPath, "geom-a.package"),
            Sims4ResourceTypeRegistry.Geom,
            0x10000000,
            meshPartA,
            BuildGeomFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "geom-b.package"),
            Sims4ResourceTypeRegistry.Geom,
            0x10000000,
            meshPartB,
            BuildGeomFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "tex-a-diffuse.package"),
            Sims4ResourceTypeRegistry.Dds,
            0x10000000,
            diffusePartA,
            BuildDdsFixture(4, 4));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "tex-a-normal.package"),
            Sims4ResourceTypeRegistry.Dds,
            0x10000000,
            normalPartA,
            BuildDdsFixture(4, 4));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "tex-a-specular.package"),
            Sims4ResourceTypeRegistry.Dds,
            0x10000000,
            specularPartA,
            BuildDdsFixture(4, 4));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "tex-b-diffuse.package"),
            Sims4ResourceTypeRegistry.Dds,
            0x10000000,
            diffusePartB,
            BuildDdsFixture(4, 4));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "tex-b-normal.package"),
            Sims4ResourceTypeRegistry.Dds,
            0x10000000,
            normalPartB,
            BuildDdsFixture(4, 4));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "tex-b-specular.package"),
            Sims4ResourceTypeRegistry.Dds,
            0x10000000,
            specularPartB,
            BuildDdsFixture(4, 4));

        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-smod.package"),
            Sims4ResourceTypeRegistry.SimModifier,
            0,
            smodInstance,
            BuildSmodFixture(bgeoInstance, dmapShapeInstance, dmapNormalInstance, bondInstance));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-sculpt.package"),
            Sims4ResourceTypeRegistry.Sculpt,
            0,
            sculptInstance,
            BuildSculptFixture(bgeoInstance, dmapShapeInstance, dmapNormalInstance, bondInstance));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-bgeo.package"),
            Sims4ResourceTypeRegistry.BlendGeometry,
            0,
            bgeoInstance,
            BuildBgeoHeaderFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-dmap-shape.package"),
            Sims4ResourceTypeRegistry.DeformerMap,
            0,
            dmapShapeInstance,
            BuildDmapHeaderFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-dmap-normal.package"),
            Sims4ResourceTypeRegistry.DeformerMap,
            0,
            dmapNormalInstance,
            BuildDmapHeaderFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-bond.package"),
            Sims4ResourceTypeRegistry.BoneDelta,
            0,
            bondInstance,
            BuildBondHeaderFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "region-a.package"),
            Sims4ResourceTypeRegistry.RegionMap,
            0,
            regionMapPartA,
            BuildRegionMapFixture(0x7200000000000001));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "region-b.package"),
            Sims4ResourceTypeRegistry.RegionMap,
            0,
            regionMapPartB,
            BuildRegionMapFixture(0x7500000000000001));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "tone.package"),
            Sims4ResourceTypeRegistry.Tone,
            0,
            toneInstance,
            BuildToneFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "pelt.package"),
            Sims4ResourceTypeRegistry.PeltLayer,
            0,
            peltLayerInstance,
            BuildPeltLayerFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "rig.package"),
            Sims4ResourceTypeRegistry.Rig,
            0,
            rigInstance,
            BuildRigFixture());

        var saveData = new SaveGameData();
        var sim = new SimData
        {
            sim_id = 0x1111,
            first_name = "Alex",
            last_name = "Tester",
            extended_species = 1,
            outfits = new OutfitList(),
            facial_attr = BuildMorphPayload(smodInstance, sculptInstance),
            skin_tone = toneInstance,
            pelt_layers = new PeltLayerDataList()
        };
        sim.pelt_layers.layers.Add(new PeltLayerData
        {
            layer_id = peltLayerInstance,
            color = 0x00FFAA
        });
        sim.outfits.outfits.Add(new OutfitData
        {
            outfit_id = 0xAAAA,
            category = 0,
            outfit_flags = 0x10,
            created = 12345,
            parts = new EA.Sims4.IdList { ids = [partA, partB] },
            body_types_list = new BodyTypesList { body_types = [5, 6] },
            part_shifts = new ColorShiftList { color_shift = [0x100, 0x200] }
        });
        saveData.sims.Add(sim);

        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        var simResult = Assert.Single(snapshot.Sims);
        Assert.Equal(0x1111UL, simResult.SimId);
        Assert.Equal("Alex Tester", simResult.FullName);
        Assert.Single(simResult.Outfits);

        var outfit = simResult.Outfits[0];
        Assert.Equal(2, outfit.Parts.Count);
        var partAResult = outfit.Parts[0];
        Assert.NotNull(partAResult.CasPart);
        Assert.NotNull(partAResult.ResolvedCasPartKey);
        Assert.NotNull(partAResult.CasPartResolution);
        Assert.NotEmpty(partAResult.TextureRefs);
        Assert.NotEmpty(partAResult.Meshes);
        Assert.NotEmpty(partAResult.Textures);
        Assert.Contains(partAResult.Meshes, mesh => mesh.Geom is { VertexCount: > 0 });
        Assert.Contains(partAResult.Textures, texture => texture.Metadata is { Width: 4, Height: 4 });

        Assert.NotEmpty(simResult.ModifierSemantics);
        Assert.False(string.IsNullOrWhiteSpace(simResult.ModifierSemantics[0].ModifierName));
        Assert.NotNull(simResult.Tone);
        Assert.NotNull(simResult.ToneRef);
        Assert.Single(simResult.PeltLayers);
        Assert.NotNull(simResult.PeltLayers[0].PeltLayer);

        Assert.NotNull(snapshot.ModifierTuningCatalog);
        Assert.NotNull(snapshot.RigBoneIndexSummary);
        Assert.True(snapshot.RigBoneIndexSummary!.BoneHashCount > 0);

        Assert.NotEmpty(snapshot.MorphGraphSummary.SimModifierLinks);
        Assert.NotEmpty(snapshot.MorphGraphSummary.SculptLinks);
        Assert.NotEmpty(snapshot.MorphGraphSummary.ReferencedResources);
        Assert.All(snapshot.MorphGraphSummary.ReferencedResources, health => Assert.True(health.Exists));
        Assert.Contains(snapshot.MorphGraphSummary.ReferencedResources, health => health.Kind == Ts4MorphReferencedResourceKind.BlendGeometry && health.HeaderParsed);
        Assert.Contains(snapshot.MorphGraphSummary.ReferencedResources, health => health.Kind == Ts4MorphReferencedResourceKind.DeformerMap && health.HeaderParsed);
        Assert.Contains(snapshot.MorphGraphSummary.ReferencedResources, health => health.Kind == Ts4MorphReferencedResourceKind.BoneDelta && health.HeaderParsed);
        Assert.Contains(snapshot.MorphGraphSummary.ReferencedResources, health => health.Kind == Ts4MorphReferencedResourceKind.BoneDelta && health.BondAdjustmentCount > 0);
        Assert.True(snapshot.ResourceStats.TotalReferences > 0);
    }

    [Fact]
    public async Task BuildSnapshotAsync_MissingCasp_ReportsResourceNotFound()
    {
        using var fixture = new TempDirectory("save-appearance-missing-casp");
        var savePath = Path.Combine(fixture.Path, "slot_00000002.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        var saveData = new SaveGameData();
        var sim = new SimData
        {
            sim_id = 0x2222,
            first_name = "Missing",
            last_name = "Part",
            extended_species = 1,
            outfits = new OutfitList()
        };
        sim.outfits.outfits.Add(new OutfitData
        {
            outfit_id = 0xBBBB,
            category = 0,
            parts = new EA.Sims4.IdList { ids = [0x9900000000000001] },
            body_types_list = new BodyTypesList { body_types = [5] },
            part_shifts = new ColorShiftList { color_shift = [0] }
        });
        saveData.sims.Add(sim);
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Contains(snapshot.Issues, issue =>
            issue.Code == "RESOURCE_NOT_FOUND" &&
            issue.Scope == Ts4AppearanceIssueScope.Part);
    }

    [Fact]
    public async Task BuildSnapshotAsync_InvalidCasp_ReportsParserFailed()
    {
        using var fixture = new TempDirectory("save-appearance-invalid-casp");
        var savePath = Path.Combine(fixture.Path, "slot_00000003.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        const ulong partId = 0x8800000000000001;
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "broken-casp.package"),
            Sims4ResourceTypeRegistry.CasPart,
            0,
            partId,
            [0x01, 0x02, 0x03, 0x04]);

        var saveData = new SaveGameData();
        var sim = new SimData
        {
            sim_id = 0x3333,
            first_name = "Broken",
            last_name = "Casp",
            extended_species = 1,
            outfits = new OutfitList()
        };
        sim.outfits.outfits.Add(new OutfitData
        {
            outfit_id = 0xCCCC,
            category = 0,
            parts = new EA.Sims4.IdList { ids = [partId] },
            body_types_list = new BodyTypesList { body_types = [5] },
            part_shifts = new ColorShiftList { color_shift = [0] }
        });
        saveData.sims.Add(sim);
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Contains(snapshot.Issues, issue =>
            issue.Code == "PARSER_FAILED" &&
            issue.Scope == Ts4AppearanceIssueScope.Part);
    }

    [Fact]
    public async Task BuildSnapshotAsync_MalformedMorphPayload_DoesNotThrowAndAddsIssue()
    {
        using var fixture = new TempDirectory("save-appearance-invalid-morph");
        var savePath = Path.Combine(fixture.Path, "slot_00000004.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        var saveData = new SaveGameData();
        saveData.sims.Add(new SimData
        {
            sim_id = 0x4444,
            first_name = "Broken",
            last_name = "Morph",
            facial_attr = [0xFF, 0x00, 0xFF, 0x00, 0x01]
        });
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Single(snapshot.Sims);
        Assert.Contains(snapshot.Issues, issue => issue.Code == "MORPH_PAYLOAD_INVALID");
    }

    [Fact]
    public async Task BuildSnapshotAsync_NonHumanSpecies_ReportsLimitationIssue()
    {
        using var fixture = new TempDirectory("save-appearance-nonhuman");
        var savePath = Path.Combine(fixture.Path, "slot_00000005.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        var saveData = new SaveGameData();
        saveData.sims.Add(new SimData
        {
            sim_id = 0x5555,
            first_name = "Wolf",
            last_name = "Form",
            extended_species = 3
        });
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Contains(snapshot.Issues, issue =>
            issue.Code == "SPECIES_LIMITATION" &&
            issue.Scope == Ts4AppearanceIssueScope.Sim &&
            issue.SimId == 0x5555UL);
    }

    [Fact]
    public async Task BuildSnapshotAsync_MissingSimInfo_ReportsInfoSeverity()
    {
        using var fixture = new TempDirectory("save-appearance-missing-siminfo");
        var savePath = Path.Combine(fixture.Path, "slot_00000006.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        var saveData = new SaveGameData();
        saveData.sims.Add(new SimData
        {
            sim_id = 0x6666,
            first_name = "No",
            last_name = "SimInfo",
            extended_species = 1
        });
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Contains(snapshot.Issues, issue =>
            issue.Code == "RESOURCE_NOT_FOUND" &&
            issue.Scope == Ts4AppearanceIssueScope.Sim &&
            issue.Severity == Ts4AppearanceIssueSeverity.Info &&
            issue.SimId == 0x6666UL);
    }

    [Fact]
    public async Task BuildSnapshotAsync_ConflictingCasp_ExposesResolutionTrace()
    {
        using var fixture = new TempDirectory("save-appearance-casp-resolution");
        var savePath = Path.Combine(fixture.Path, "slot_00000007.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        const ulong partId = 0x9900000000000001;
        var caspBytes = BuildCaspFixture(
            diffuseInstance: 0x7100000000000101,
            normalInstance: 0x7100000000000102,
            specularInstance: 0x7100000000000103,
            meshInstance: 0x7200000000000101,
            regionMapInstance: 0x7300000000000101);
        WritePackageWithSingleResource(
            Path.Combine(gamePath, "part-game.package"),
            Sims4ResourceTypeRegistry.CasPart,
            0,
            partId,
            caspBytes);
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "part-mod.package"),
            Sims4ResourceTypeRegistry.CasPart,
            0,
            partId,
            caspBytes);

        var saveData = new SaveGameData();
        var sim = new SimData
        {
            sim_id = 0x7777,
            first_name = "Trace",
            last_name = "Candidate",
            extended_species = 1,
            outfits = new OutfitList()
        };
        sim.outfits.outfits.Add(new OutfitData
        {
            outfit_id = 0xDDDD,
            category = 0,
            parts = new EA.Sims4.IdList { ids = [partId] },
            body_types_list = new BodyTypesList { body_types = [5] },
            part_shifts = new ColorShiftList { color_shift = [0] }
        });
        saveData.sims.Add(sim);
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        var resolvedPart = Assert.Single(Assert.Single(Assert.Single(snapshot.Sims).Outfits).Parts);
        var resolution = Assert.IsType<Ts4ResourceResolution>(resolvedPart.CasPartResolution);
        Assert.True(resolution.Found);
        Assert.True(resolution.Candidates.Count >= 2);
        Assert.Equal(Ts4ResourceSourceKind.Mods, resolution.Candidates[0].SourceKind);
        Assert.Contains(resolution.Candidates, candidate => candidate.SourceKind == Ts4ResourceSourceKind.Game);
    }

    [Fact]
    public async Task BuildSnapshotAsync_BrokenGeomAndTexture_AddsIssuesWithoutStoppingSnapshot()
    {
        using var fixture = new TempDirectory("save-appearance-broken-geom-texture");
        var savePath = Path.Combine(fixture.Path, "slot_00000008.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        const ulong partId = 0x9800000000000001;
        const ulong meshId = 0x7200000000000201;
        const ulong diffuseId = 0x7100000000000201;
        const ulong normalId = 0x7100000000000202;
        const ulong specularId = 0x7100000000000203;
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "part.package"),
            Sims4ResourceTypeRegistry.CasPart,
            0,
            partId,
            BuildCaspFixture(
                diffuseInstance: diffuseId,
                normalInstance: normalId,
                specularInstance: specularId,
                meshInstance: meshId,
                regionMapInstance: 0x7300000000000201,
                textureType: Sims4ResourceTypeRegistry.Rle2));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "mesh-broken.package"),
            Sims4ResourceTypeRegistry.Geom,
            0x10000000,
            meshId,
            [0x01, 0x02, 0x03, 0x04]);
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "texture-decode-fail.package"),
            Sims4ResourceTypeRegistry.Rle2,
            0x10000000,
            diffuseId,
            BuildRle2L8Fixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "texture-parse-fail.package"),
            Sims4ResourceTypeRegistry.Rle2,
            0x10000000,
            normalId,
            [0xFF, 0x00, 0xAA]);

        var saveData = new SaveGameData();
        var sim = new SimData
        {
            sim_id = 0x8888,
            first_name = "Broken",
            last_name = "Assets",
            extended_species = 1,
            outfits = new OutfitList()
        };
        sim.outfits.outfits.Add(new OutfitData
        {
            outfit_id = 0xEEEE,
            category = 0,
            parts = new EA.Sims4.IdList { ids = [partId] },
            body_types_list = new BodyTypesList { body_types = [5] },
            part_shifts = new ColorShiftList { color_shift = [0] }
        });
        saveData.sims.Add(sim);
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Single(snapshot.Sims);
        Assert.Contains(snapshot.Issues, issue => issue.Code == "GEOM_PARSE_FAILED");
        Assert.Contains(snapshot.Issues, issue => issue.Code == "TEXTURE_PARSE_FAILED");
        Assert.Contains(snapshot.Issues, issue => issue.Code == "TEXTURE_DECODE_FAILED");
    }

    private static byte[] BuildMorphPayload(ulong simModifierInstance, ulong sculptInstance)
    {
        var payload = new BlobSimFacialCustomizationData
        {
            sculpts = [sculptInstance]
        };
        payload.face_modifiers.Add(new Modifier
        {
            key = simModifierInstance,
            amount = 0.75f
        });

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, payload);
        return stream.ToArray();
    }

    private static byte[] BuildCaspFixture(
        ulong diffuseInstance,
        ulong normalInstance,
        ulong specularInstance,
        ulong meshInstance,
        ulong regionMapInstance,
        uint textureType = Sims4ResourceTypeRegistry.Dds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        using var beWriter = new BinaryWriter(stream, Encoding.BigEndianUnicode, leaveOpen: true);

        writer.Write(32u);
        writer.Write(0u);
        writer.Write(0);
        beWriter.Write("Appearance Part");
        writer.Write(1.0f);
        writer.Write((ushort)0);
        writer.Write(0xDEAD0001u);
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write(0ul);
        writer.Write(0u);
        writer.Write(0);
        writer.Write(0u);
        writer.Write(0x11111111u);
        writer.Write(0x22222222u);
        writer.Write((byte)0);
        writer.Write(2u);
        writer.Write(0u);
        writer.Write(0x00002010u);
        writer.Write(1u);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0ul);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write((byte)1);
        writer.Write(0ul);
        writer.Write(0u);
        writer.Write((byte)1);
        writer.Write((byte)1);
        writer.Write((byte)1);
        writer.Write((byte)byte.MaxValue);
        writer.Write((byte)2);
        writer.Write((byte)byte.MaxValue);
        writer.Write((byte)0);
        writer.Write((byte)5);
        writer.Write((byte)0);
        writer.Write((byte)3);
        writer.Write((byte)4);
        writer.Write(0u);
        writer.Write((byte)byte.MaxValue);
        writer.Write((byte)6);

        WriteIgt(writer, 0, 0, 0);
        WriteIgt(writer, Sims4ResourceTypeRegistry.Geom, 0x10000000, meshInstance);
        WriteIgt(writer, textureType, 0x10000000, diffuseInstance);
        WriteIgt(writer, textureType, 0x10000000, normalInstance);
        WriteIgt(writer, textureType, 0x10000000, specularInstance);
        WriteIgt(writer, Sims4ResourceTypeRegistry.RegionMap, 0x10000000, regionMapInstance);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildSmodFixture(ulong bgeoInstance, ulong dmapShapeInstance, ulong dmapNormalInstance, ulong bondInstance)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(1u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(0u);
        WriteItg(writer, Sims4ResourceTypeRegistry.BlendGeometry, 0, bgeoInstance);
        writer.Write(144u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        WriteItg(writer, Sims4ResourceTypeRegistry.BoneDelta, 0, bondInstance);
        WriteItg(writer, Sims4ResourceTypeRegistry.DeformerMap, 0, dmapShapeInstance);
        WriteItg(writer, Sims4ResourceTypeRegistry.DeformerMap, 0, dmapNormalInstance);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildGeomFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(3);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0ul);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0x4D4F4547u);
        writer.Write(14);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0u);
        writer.Write(1);
        writer.Write(0);
        writer.Write(2);
        writer.Write(1);
        writer.Write(1);
        writer.Write(2);
        writer.Write((byte)12);
        writer.Write(new byte[24]);
        writer.Write(1);
        writer.Write((byte)2);
        writer.Write(6);
        writer.Write(new byte[12]);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(2);
        writer.Write(0x11111111u);
        writer.Write(0x22222222u);
        writer.Write(1);
        WriteItg(writer, Sims4ResourceTypeRegistry.Dds, 0x10000000, 0x7100000000000001);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildDdsFixture(int width, int height, int mipCount = 1)
    {
        var bytes = new byte[128];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0x20534444u);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), 124);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), height);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, 4), mipCount);
        return bytes;
    }

    private static byte[] BuildRle2L8Fixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x2020384Cu);
        writer.Write(0x32454C52u);
        writer.Write((ushort)4);
        writer.Write((ushort)4);
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write(24);
        writer.Write(24);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildSculptFixture(ulong bgeoInstance, ulong dmapShapeInstance, ulong dmapNormalInstance, ulong bondInstance)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(1u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(0u);
        WriteItg(writer, Sims4ResourceTypeRegistry.BlendGeometry, 0, bgeoInstance);
        writer.Write(0x61u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        WriteItg(writer, Sims4ResourceTypeRegistry.Dds, 0, 0x7770000000000001);
        WriteItg(writer, Sims4ResourceTypeRegistry.Dds, 0, 0x7770000000000002);
        WriteItg(writer, Sims4ResourceTypeRegistry.Dds, 0, 0x7770000000000003);
        writer.Write((byte)0);
        WriteItg(writer, Sims4ResourceTypeRegistry.DeformerMap, 0, dmapShapeInstance);
        WriteItg(writer, Sims4ResourceTypeRegistry.DeformerMap, 0, dmapNormalInstance);
        WriteItg(writer, Sims4ResourceTypeRegistry.BoneDelta, 0, bondInstance);
        writer.Write(0u);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildBgeoHeaderFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(3u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0x4F454742u);
        writer.Write(0x00000600u);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildDmapHeaderFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(7u);
        writer.Write(4u);
        writer.Write(2u);
        writer.Write(0x00002010u);
        writer.Write(1u);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write((byte)1);
        writer.Write(-0.2f);
        writer.Write(0.4f);
        writer.Write(0);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildBondHeaderFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(3u);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        WriteItg(writer, Sims4ResourceTypeRegistry.BoneDelta, 0, 0x9000000000000001);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write(0x11111111u);
        writer.Write(1f);
        writer.Write(2f);
        writer.Write(3f);
        writer.Write(1f);
        writer.Write(1f);
        writer.Write(1f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(1f);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildRegionMapFixture(ulong meshInstance)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(3u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(44u);
        writer.Write(21u);
        writer.Write(1);
        writer.Write(1);
        writer.Write(5u);
        writer.Write(0.25f);
        writer.Write((byte)1);
        writer.Write(1u);
        WriteItg(writer, Sims4ResourceTypeRegistry.Geom, 0x10000000, meshInstance);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildToneFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(11u);
        writer.Write((byte)1);
        writer.Write(0x111ul);
        writer.Write(0x222ul);
        writer.Write(1.2f);
        writer.Write(0.3f);
        writer.Write(0.4f);
        writer.Write(1);
        writer.Write(0x00002010u);
        writer.Write(0x333ul);
        writer.Write((ushort)30);
        writer.Write((ushort)40);
        writer.Write(255u);
        writer.Write(1);
        writer.Write((ushort)0x0045);
        writer.Write(0x51u);
        writer.Write((byte)1);
        writer.Write(unchecked((int)0xFF112233));
        writer.Write(5f);
        writer.Write(0x260A5ul);
        writer.Write((ushort)2);
        writer.Write(-0.1f);
        writer.Write(0.1f);
        writer.Write(0.01f);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildPeltLayerFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(8u);
        writer.Write(77u);
        writer.Write(2.5f);
        writer.Write((byte)3);
        writer.Write(0x444ul);
        writer.Write(0x12345678u);
        writer.Write(new byte[] { 1, 2, 3, 4, 5 });
        writer.Write(0x555ul);
        writer.Write(0x666ul);
        writer.Write(1u);
        writer.Write((ushort)0x20);
        writer.Write(0x1234u);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildRigFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(3);
        writer.Write(1);
        writer.Write(2);
        WriteRigBone(writer, "Root", -1, 0x11111111u);
        WriteRigBone(writer, "Spine", 0, 0x22222222u);
        writer.Write(0);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteRigBone(BinaryWriter writer, string name, int parentIndex, uint hash)
    {
        for (var i = 0; i < 10; i++)
        {
            writer.Write(i == 6 ? 1f : 0f);
        }

        writer.Write(name.Length);
        writer.Write(name.ToCharArray());
        writer.Write(0);
        writer.Write(parentIndex);
        writer.Write(hash);
        writer.Write(0u);
    }

    private static void WriteItg(BinaryWriter writer, uint type, uint group, ulong instance)
    {
        writer.Write(instance);
        writer.Write(type);
        writer.Write(group);
    }

    private static void WriteIgt(BinaryWriter writer, uint type, uint group, ulong instance)
    {
        writer.Write(instance);
        writer.Write(group);
        writer.Write(type);
    }

    private static void WriteSavePackage(string savePath, SaveGameData saveData)
    {
        using var payload = new MemoryStream();
        Serializer.Serialize(payload, saveData);
        WritePackageWithSingleResource(savePath, 0x0000000D, 0, 1, payload.ToArray());
    }

    private static void WritePackageWithSingleResource(string packagePath, uint type, uint group, ulong instance, byte[] bytes)
    {
        const int headerSize = 96;
        const int flagsSize = 4;
        const int indexEntrySize = 32;

        var indexPosition = headerSize;
        var recordSize = flagsSize + indexEntrySize;
        var resourcePosition = headerSize + recordSize;

        var header = new byte[headerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 1179664964u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), 2u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(36, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40, 4), (uint)indexPosition);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44, 4), (uint)recordSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(64, 8), (ulong)indexPosition);

        var index = new byte[recordSize];
        var entry = index.AsSpan(flagsSize, indexEntrySize);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(0, 4), type);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(4, 4), group);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8, 4), (uint)(instance >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(12, 4), (uint)(instance & 0xFFFFFFFF));
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(16, 4), (uint)resourcePosition);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(20, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(24, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(28, 2), 0);

        using var stream = File.Create(packagePath);
        stream.Write(header);
        stream.Write(index);
        stream.Write(bytes);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
