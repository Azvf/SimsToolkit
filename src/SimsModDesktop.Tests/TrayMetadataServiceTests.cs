using System.Buffers.Binary;
using System.Text;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class TrayMetadataServiceTests
{
    [Fact]
    public async Task GetMetadataAsync_ParsesStructuredHouseholdMetadata()
    {
        using var temp = new TempDirectory();
        var trayItemPath = Path.Combine(temp.Path, "0x1.trayitem");

        File.WriteAllBytes(
            trayItemPath,
            BuildTrayItemFile(
                BuildTrayMetadataMessage(
                    type: 1,
                    name: "Prescott",
                    description: "A longer household description for metadata testing.",
                    creatorId: 123456789,
                    creatorName: "ParaDIseLoSTI",
                    specificData: BuildSpecificDataForHousehold(
                        familySize: 2,
                        pendingBabies: 1,
                        isModdedContent: true,
                        sims:
                        [
                            ("Alice", "Prescott"),
                            ("Bob", "Prescott")
                        ]))));

        var service = new TrayMetadataService();
        var results = await service.GetMetadataAsync([trayItemPath]);

        Assert.True(results.TryGetValue(Path.GetFullPath(trayItemPath), out var metadata));
        Assert.Equal("Prescott", metadata!.Name);
        Assert.Equal("ParaDIseLoSTI", metadata.CreatorName);
        Assert.Equal("123456789", metadata.CreatorId);
        Assert.Equal("A longer household description for metadata testing.", metadata.Description);
        Assert.Equal("Household", metadata.ItemType);
        Assert.Equal(2, metadata.FamilySize);
        Assert.Equal(1, metadata.PendingBabies);
        Assert.True(metadata.IsModdedContent);
        Assert.Equal(2, metadata.Members.Count);
        Assert.Equal("Alice Prescott", metadata.Members[0].FullName);
        Assert.Equal("Bob Prescott", metadata.Members[1].FullName);
    }

    [Fact]
    public async Task GetMetadataAsync_RefreshesCache_WhenFileChanges()
    {
        using var temp = new TempDirectory();
        var trayItemPath = Path.Combine(temp.Path, "0x2.trayitem");
        File.WriteAllBytes(
            trayItemPath,
            BuildTrayItemFile(
                BuildTrayMetadataMessage(
                    type: 1,
                    name: "First Name",
                    specificData: BuildSpecificDataForHousehold(
                        familySize: 1,
                        pendingBabies: 0,
                        isModdedContent: false,
                        sims:
                        [
                            ("First", "Sim")
                        ]))));

        var service = new TrayMetadataService();
        var first = await service.GetMetadataAsync([trayItemPath]);
        Assert.Equal("First Name", first[Path.GetFullPath(trayItemPath)].Name);

        File.WriteAllBytes(
            trayItemPath,
            BuildTrayItemFile(
                BuildTrayMetadataMessage(
                    type: 1,
                    name: "Second Name",
                    specificData: BuildSpecificDataForHousehold(
                        familySize: 1,
                        pendingBabies: 0,
                        isModdedContent: false,
                        sims:
                        [
                            ("Second", "Sim")
                        ]))));
        File.SetLastWriteTimeUtc(trayItemPath, DateTime.UtcNow.AddSeconds(2));

        var second = await service.GetMetadataAsync([trayItemPath]);
        Assert.Equal("Second Name", second[Path.GetFullPath(trayItemPath)].Name);
    }

    [Fact]
    public async Task GetMetadataAsync_UsesStructuredNestedPayload_WhenTypeFieldIsMissing()
    {
        using var temp = new TempDirectory();
        var trayItemPath = Path.Combine(temp.Path, "0x3.trayitem");

        File.WriteAllBytes(
            trayItemPath,
            BuildTrayItemFile(
                BuildTrayMetadataMessage(
                    type: 0,
                    name: "Nobu Malibu",
                    specificData: BuildSpecificDataForBlueprint(
                        sizeX: 30,
                        sizeZ: 20,
                        priceValue: 184320,
                        numBedrooms: 3,
                        numBathrooms: 2))));

        var service = new TrayMetadataService();
        var results = await service.GetMetadataAsync([trayItemPath]);

        Assert.True(results.TryGetValue(Path.GetFullPath(trayItemPath), out var metadata));
        Assert.Equal("Lot", metadata!.ItemType);
        Assert.Equal(30, metadata.SizeX);
        Assert.Equal(20, metadata.SizeZ);
        Assert.Equal(184320, metadata.PriceValue);
        Assert.Equal(3, metadata.NumBedrooms);
        Assert.Equal(2, metadata.NumBathrooms);
    }

    [Fact]
    public async Task GetMetadataAsync_ParsesAdditionalExactBlueprintAndSpecificFields()
    {
        using var temp = new TempDirectory();
        var trayItemPath = Path.Combine(temp.Path, "0x4.trayitem");

        var blueprint = new List<byte>();
        WriteVarintField(blueprint, 1, 3001);
        WriteVarintField(blueprint, 2, 40);
        WriteVarintField(blueprint, 3, 30);
        WriteVarintField(blueprint, 4, 3);
        WriteVarintField(blueprint, 5, 250000);
        WriteVarintField(blueprint, 6, 4);
        WriteVarintField(blueprint, 7, 3);
        WriteVarintField(blueprint, 8, 777);
        WriteVarintField(blueprint, 9, 5);
        WriteVarintField(blueprint, 10, 2);
        WriteVarintField(blueprint, 11, 6001);
        WriteVarintField(blueprint, 12, 1);
        WritePackedVarintField(blueprint, 13, 7001, 7002);
        WritePackedFixed64Field(blueprint, 14, 0x1111111111111111, 0x2222222222222222);
        WriteVarintField(blueprint, 15, 9);
        WriteVarintField(blueprint, 16, 0xABCDEF);
        WriteMessageField(blueprint, 17, [0x08, 0x01]);
        WriteVarintField(blueprint, 18, 960);
        WriteVarintField(blueprint, 19, 2);
        WriteMessageField(blueprint, 20, [0x08, 0x01]);
        WritePackedVarintField(blueprint, 21, 12, 24);

        var specific = new List<byte>();
        WriteMessageField(specific, 1, [.. blueprint]);
        WriteVarintField(specific, 3, 1);
        WriteVarintField(specific, 4, 1);
        WriteVarintField(specific, 5, 1);
        WriteStringField(specific, 8, "#alpha #beta");
        WriteVarintField(specific, 9, 1033);
        WriteVarintField(specific, 10, 4000);
        WriteVarintField(specific, 11, 1);
        WriteVarintField(specific, 12, 8192);
        WriteVarintField(specific, 13, 1);
        WriteVarintField(specific, 14, 1);
        WriteVarintField(specific, 15, 1);
        WriteVarintField(specific, 16, 2);
        WriteVarintField(specific, 17, 3);
        WriteVarintField(specific, 18, 9999);
        WriteVarintField(specific, 19, 8888);
        WriteVarintField(specific, 20, 1);
        WriteVarintField(specific, 21, 0);
        WriteStringField(specific, 22, "Curated");
        WriteVarintField(specific, 23, 5000);
        WritePackedVarintField(specific, 24, 111, 222, 333);
        WriteVarintField(specific, 25, 1);
        WriteVarintField(specific, 1001, 11300);

        var message = new List<byte>();
        WriteVarintField(message, 1, 0x12345);
        WriteVarintField(message, 2, 2);
        WriteStringField(message, 4, "Expanded Lot");
        WriteStringField(message, 5, "Detailed lot metadata.");
        WriteVarintField(message, 6, 42);
        WriteStringField(message, 7, "Builder");
        WriteVarintField(message, 8, 10);
        WriteVarintField(message, 9, 20);
        WriteMessageField(message, 10, [.. specific]);
        WriteVarintField(message, 11, 123456789);
        WritePackedVarintField(message, 12, 101, 202);
        WriteVarintField(message, 14, 84);
        WriteStringField(message, 15, "Moderator");
        WritePackedVarintField(message, 16, 7, 8, 9);
        WriteVarintField(message, 17, 77);
        WriteVarintField(message, 20, 3);
        WriteVarintField(message, 21, 1);
        WriteVarintField(message, 25, 9);
        WriteVarintField(message, 26, 2);
        WriteVarintField(message, 27, 3);
        WriteVarintField(message, 28, 100);
        WriteStringField(message, 29, "Steam");
        WriteVarintField(message, 30, 200);
        WriteStringField(message, 31, "EA App");
        WriteVarintField(message, 32, 2);
        WriteVarintField(message, 33, 987654321);
        WriteVarintField(message, 34, 1);

        File.WriteAllBytes(trayItemPath, BuildTrayItemFile([.. message]));

        var service = new TrayMetadataService();
        var results = await service.GetMetadataAsync([trayItemPath]);

        Assert.True(results.TryGetValue(Path.GetFullPath(trayItemPath), out var metadata));
        Assert.Equal("74565", metadata!.TrayMetadataId);
        Assert.Equal((ulong)10, metadata.Favorites);
        Assert.Equal((ulong)20, metadata.Downloads);
        Assert.Equal((ulong)123456789, metadata.ItemTimestamp);
        Assert.Equal(new ulong[] { 101, 202 }, metadata.MtxIds);
        Assert.Equal("84", metadata.ModifierId);
        Assert.Equal("Moderator", metadata.ModifierName);
        Assert.Equal(new uint[] { 7, 8, 9 }, metadata.MetaInfo);
        Assert.Equal(77, metadata.VerifyCode);
        Assert.Equal((uint)3, metadata.CustomImageCount);
        Assert.Equal((uint)1, metadata.MannequinCount);
        Assert.Equal((ulong)9, metadata.IndexedCounter);
        Assert.Equal((uint)2, metadata.CreatorPlatform);
        Assert.Equal("Steam", metadata.CreatorPlatformName);
        Assert.Equal((uint)3, metadata.ModifierPlatform);
        Assert.Equal("EA App", metadata.ModifierPlatformName);
        Assert.Equal((ulong)987654321, metadata.SharedTimestamp);
        Assert.True(metadata.Liked.GetValueOrDefault());
        Assert.True(metadata.IsHidden.GetValueOrDefault());
        Assert.True(metadata.IsDownloadTemp.GetValueOrDefault());
        Assert.Equal("#alpha #beta", metadata.DescriptionHashtags);
        Assert.Equal((ulong)1033, metadata.LanguageId);
        Assert.Equal((ulong)4000, metadata.SkuId);
        Assert.True(metadata.IsMaxisContent.GetValueOrDefault());
        Assert.Equal((uint)8192, metadata.PayloadSize);
        Assert.True(metadata.WasReported.GetValueOrDefault());
        Assert.True(metadata.WasReviewedAndCleared.GetValueOrDefault());
        Assert.True(metadata.IsImageModdedContent.GetValueOrDefault());
        Assert.Equal((uint)2, metadata.SpecificCreatorPlatform);
        Assert.Equal((uint)3, metadata.SpecificModifierPlatform);
        Assert.Equal((ulong)9999, metadata.SpecificCreatorPlatformPersonaId);
        Assert.Equal((ulong)8888, metadata.SpecificModifierPlatformPersonaId);
        Assert.True(metadata.IsCgItem.GetValueOrDefault());
        Assert.False(metadata.IsCgInterested.GetValueOrDefault());
        Assert.Equal("Curated", metadata.CgName);
        Assert.Equal((ulong)5000, metadata.Sku2Id);
        Assert.Equal(new uint[] { 111, 222, 333 }, metadata.CdsPatchBaseChangelists);
        Assert.True(metadata.CdsContentPatchMounted.GetValueOrDefault());
        Assert.Equal((uint)11300, metadata.SpecificDataVersion);
        Assert.Equal((ulong)3001, metadata.VenueType);
        Assert.Equal((uint)3, metadata.PriceLevel);
        Assert.Equal((uint)777, metadata.ArchitectureValue);
        Assert.Equal((uint)5, metadata.NumThumbnails);
        Assert.Equal((uint)2, metadata.FrontSide);
        Assert.Equal((uint)6001, metadata.VenueTypeStringKey);
        Assert.Equal((uint)1, metadata.GroundFloorIndex);
        Assert.Equal(new uint[] { 7001, 7002 }, metadata.OptionalRuleSatisfiedStringKeys);
        Assert.Equal(new ulong[] { 0x1111111111111111, 0x2222222222222222 }, metadata.LotTraits);
        Assert.Equal((uint)9, metadata.BuildingType);
        Assert.Equal((ulong)0xABCDEF, metadata.LotTemplateId);
        Assert.True(metadata.HasUniversityHousingConfiguration);
        Assert.Equal((uint)960, metadata.TileCount);
        Assert.Equal((uint)2, metadata.UnitCount);
        Assert.Equal(1, metadata.UnitTraitCount);
        Assert.Equal(new uint[] { 12, 24 }, metadata.DynamicAreas);
    }

    [Fact]
    public async Task GetMetadataAsync_ParsesAdditionalExactSimFields()
    {
        using var temp = new TempDirectory();
        var trayItemPath = Path.Combine(temp.Path, "0x5.trayitem");

        var sim = new List<byte>();
        WriteStringField(sim, 3, "Alice");
        WriteStringField(sim, 4, "Prescott");
        WriteVarintField(sim, 5, 987654321);
        WriteVarintField(sim, 6, 2);
        WriteVarintField(sim, 7, 4321);
        WriteVarintField(sim, 9, 4);
        WriteVarintField(sim, 12, 1);
        WriteVarintField(sim, 13, 1);
        WriteVarintField(sim, 14, 8);
        WriteStringField(sim, 15, "Tabby");
        WriteVarintField(sim, 16, 777);
        WriteMessageField(sim, 17, BuildRankedStatMessage(55, 4.5f));
        WriteVarintField(sim, 24, 9001);

        var household = new List<byte>();
        WriteVarintField(household, 1, 1);
        WriteMessageField(household, 2, [.. sim]);

        var specific = new List<byte>();
        WriteMessageField(specific, 2, [.. household]);

        File.WriteAllBytes(
            trayItemPath,
            BuildTrayItemFile(
                BuildTrayMetadataMessage(
                    type: 1,
                    name: "Detailed Household",
                    specificData: [.. specific])));

        var service = new TrayMetadataService();
        var results = await service.GetMetadataAsync([trayItemPath]);

        Assert.True(results.TryGetValue(Path.GetFullPath(trayItemPath), out var metadata));
        var member = Assert.Single(metadata!.Members);
        Assert.Equal("Alice Prescott", member.FullName);
        Assert.Equal("987654321", member.SimId);
        Assert.Equal((uint)2, member.Gender);
        Assert.Equal((ulong)4321, member.AspirationId);
        Assert.Equal((uint)4, member.Age);
        Assert.Equal((uint)1, member.Species);
        Assert.True(member.IsCustomGender.GetValueOrDefault());
        Assert.Equal((uint)8, member.OccultTypes);
        Assert.Equal((uint)777, member.BreedNameKey);
        Assert.Equal((ulong)55, member.FameRankedStatId);
        Assert.Equal(4.5f, member.FameValue.GetValueOrDefault());
        Assert.Equal((ulong)9001, member.DeathTrait);
    }

    private static byte[] BuildTrayItemFile(byte[] metadataPayload, uint headerType = 0)
    {
        var result = new byte[8 + metadataPayload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), headerType);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)metadataPayload.Length);
        metadataPayload.CopyTo(result.AsSpan(8));
        return result;
    }

    private static byte[] BuildTrayMetadataMessage(
        int type,
        string name,
        string? description = null,
        ulong creatorId = 0,
        string? creatorName = null,
        byte[]? specificData = null)
    {
        var bytes = new List<byte>();
        WriteVarintField(bytes, 2, (ulong)type);
        WriteStringField(bytes, 4, name);

        if (!string.IsNullOrWhiteSpace(description))
        {
            WriteStringField(bytes, 5, description);
        }

        if (creatorId > 0)
        {
            WriteVarintField(bytes, 6, creatorId);
        }

        if (!string.IsNullOrWhiteSpace(creatorName))
        {
            WriteStringField(bytes, 7, creatorName);
        }

        if (specificData is not null)
        {
            WriteMessageField(bytes, 10, specificData);
        }

        return [.. bytes];
    }

    private static byte[] BuildSpecificDataForHousehold(
        int familySize,
        int pendingBabies,
        bool isModdedContent,
        IReadOnlyList<(string FirstName, string LastName)> sims)
    {
        var household = new List<byte>();
        WriteVarintField(household, 1, (ulong)familySize);
        foreach (var sim in sims)
        {
            WriteMessageField(household, 2, BuildSimMessage(sim.FirstName, sim.LastName));
        }

        if (pendingBabies > 0)
        {
            WriteVarintField(household, 3, (ulong)pendingBabies);
        }

        var specific = new List<byte>();
        WriteMessageField(specific, 2, [.. household]);
        if (isModdedContent)
        {
            WriteVarintField(specific, 5, 1);
        }

        return [.. specific];
    }

    private static byte[] BuildSpecificDataForBlueprint(
        int sizeX,
        int sizeZ,
        int priceValue,
        int numBedrooms,
        int numBathrooms)
    {
        var blueprint = new List<byte>();
        WriteVarintField(blueprint, 2, (ulong)sizeX);
        WriteVarintField(blueprint, 3, (ulong)sizeZ);
        WriteVarintField(blueprint, 5, (ulong)priceValue);
        WriteVarintField(blueprint, 6, (ulong)numBedrooms);
        WriteVarintField(blueprint, 7, (ulong)numBathrooms);

        var specific = new List<byte>();
        WriteMessageField(specific, 1, [.. blueprint]);
        return [.. specific];
    }

    private static byte[] BuildSimMessage(string firstName, string lastName)
    {
        var bytes = new List<byte>();
        WriteStringField(bytes, 3, firstName);
        WriteStringField(bytes, 4, lastName);
        return [.. bytes];
    }

    private static void WriteStringField(List<byte> buffer, int fieldNumber, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteFieldHeader(buffer, fieldNumber, 2);
        WriteVarint(buffer, (ulong)bytes.Length);
        buffer.AddRange(bytes);
    }

    private static void WriteMessageField(List<byte> buffer, int fieldNumber, byte[] payload)
    {
        WriteFieldHeader(buffer, fieldNumber, 2);
        WriteVarint(buffer, (ulong)payload.Length);
        buffer.AddRange(payload);
    }

    private static void WriteVarintField(List<byte> buffer, int fieldNumber, ulong value)
    {
        WriteFieldHeader(buffer, fieldNumber, 0);
        WriteVarint(buffer, value);
    }

    private static void WritePackedVarintField(List<byte> buffer, int fieldNumber, params ulong[] values)
    {
        var payload = new List<byte>();
        foreach (var value in values)
        {
            WriteVarint(payload, value);
        }

        WriteMessageField(buffer, fieldNumber, [.. payload]);
    }

    private static void WritePackedFixed64Field(List<byte> buffer, int fieldNumber, params ulong[] values)
    {
        var payload = new byte[values.Length * sizeof(ulong)];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                payload.AsSpan(i * sizeof(ulong), sizeof(ulong)),
                values[i]);
        }

        WriteMessageField(buffer, fieldNumber, payload);
    }

    private static byte[] BuildRankedStatMessage(ulong id, float value)
    {
        var bytes = new List<byte>();
        WriteVarintField(bytes, 1, id);
        WriteFixed32Field(bytes, 2, BitConverter.SingleToUInt32Bits(value));
        return [.. bytes];
    }

    private static void WriteFieldHeader(List<byte> buffer, int fieldNumber, int wireType)
    {
        WriteVarint(buffer, (ulong)((fieldNumber << 3) | wireType));
    }

    private static void WriteFixed32Field(List<byte> buffer, int fieldNumber, uint value)
    {
        WriteFieldHeader(buffer, fieldNumber, 5);
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        buffer.AddRange(bytes.ToArray());
    }

    private static void WriteVarint(List<byte> buffer, ulong value)
    {
        do
        {
            var current = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                current |= 0x80;
            }

            buffer.Add(current);
        } while (value != 0);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tray-meta-{Guid.NewGuid():N}");
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
