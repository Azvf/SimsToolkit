using System.Buffers.Binary;
using System.Security.Cryptography;
using EA.Sims4.Persistence;
using EA.Sims4.Exchange;
using ProtoBuf;
using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.SaveData.Services;

public sealed class HouseholdTrayExporter : IHouseholdTrayExporter
{
    private const uint TrayItemHeaderType = 1;
    private const uint HouseholdBinaryType = 2;
    private const uint HouseholdPortraitPrimaryType = 0x10;
    private const uint HouseholdPortraitSecondaryType = 0x11;
    private const int MaxHouseholdMembers = 8;
    private static readonly byte[] PlaceholderPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9q24gAAAAASUVORK5CYII=");

    private readonly ISaveHouseholdReader _saveHouseholdReader;

    public HouseholdTrayExporter(ISaveHouseholdReader saveHouseholdReader)
    {
        _saveHouseholdReader = saveHouseholdReader;
    }

    public SaveHouseholdExportResult Export(SaveHouseholdExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (string.IsNullOrWhiteSpace(request.ExportRootPath) || !Directory.Exists(request.ExportRootPath))
            {
                return Failed("Export root path does not exist.");
            }

            var snapshot = _saveHouseholdReader.Load(request.SourceSavePath);
            var household = snapshot.Households.FirstOrDefault(item => item.HouseholdId == request.HouseholdId);
            if (household is null)
            {
                return Failed("The requested household was not found in the source save.");
            }

            if (!household.CanExport)
            {
                return Failed(string.IsNullOrWhiteSpace(household.ExportBlockReason)
                    ? "This household cannot be exported."
                    : household.ExportBlockReason);
            }

            if (!snapshot.RawHouseholds.TryGetValue(household.HouseholdId, out var rawHousehold))
            {
                return Failed("The raw household payload was not available for export.");
            }

            var instanceId = GenerateInstanceId();
            var exportDirectory = CreateUniqueExportDirectory(request.ExportRootPath, household.Name, household.HouseholdId);
            var writtenFiles = new List<string>();
            var warnings = new List<string>();

            try
            {
                var trayItemPath = Path.Combine(exportDirectory, BuildFileName(TrayItemHeaderType, instanceId, ".trayitem"));
                WriteTrayItem(trayItemPath, instanceId, household, request);
                writtenFiles.Add(trayItemPath);

                var householdBinaryPath = Path.Combine(exportDirectory, BuildFileName(HouseholdBinaryType, instanceId, ".householdbinary"));
                WriteHouseholdBinary(householdBinaryPath, rawHousehold);
                writtenFiles.Add(householdBinaryPath);

                var portraitPrimaryPath = Path.Combine(exportDirectory, BuildFileName(HouseholdPortraitPrimaryType, instanceId, ".hhi"));
                WritePlaceholderImage(portraitPrimaryPath);
                writtenFiles.Add(portraitPrimaryPath);

                var portraitSecondaryPath = Path.Combine(exportDirectory, BuildFileName(HouseholdPortraitSecondaryType, instanceId, ".hhi"));
                WritePlaceholderImage(portraitSecondaryPath);
                writtenFiles.Add(portraitSecondaryPath);

                if (request.GenerateThumbnails)
                {
                    var slotCount = Math.Min(household.HouseholdSize, MaxHouseholdMembers);
                    for (var slot = 1; slot <= slotCount; slot++)
                    {
                        var sgiType = BuildSimGlyphType(slot);
                        var sgiPath = Path.Combine(exportDirectory, BuildFileName(sgiType, instanceId, ".sgi"));
                        WritePlaceholderImage(sgiPath);
                        writtenFiles.Add(sgiPath);
                    }
                }
                else
                {
                    warnings.Add("Member thumbnails were disabled; .sgi placeholders were not generated.");
                }

                var expectedSgiCount = request.GenerateThumbnails ? Math.Min(household.HouseholdSize, MaxHouseholdMembers) : 0;
                if (!ValidateWrittenFiles(writtenFiles, expectedSgiCount))
                {
                    SafeDeleteDirectory(exportDirectory);
                    return Failed("The exported tray bundle is incomplete.");
                }

                return new SaveHouseholdExportResult
                {
                    Succeeded = true,
                    ExportDirectory = exportDirectory,
                    InstanceIdHex = $"0x{instanceId:X16}",
                    WrittenFiles = writtenFiles,
                    Warnings = warnings
                };
            }
            catch (Exception ex)
            {
                SafeDeleteDirectory(exportDirectory);
                return Failed(ex.Message);
            }
        }
        catch (Exception ex)
        {
            return Failed(ex.Message);
        }
    }

    private void WriteTrayItem(
        string trayItemPath,
        ulong instanceId,
        SaveHouseholdItem household,
        SaveHouseholdExportRequest request)
    {
        var payload = BuildTrayMetadata(instanceId, household, request);
        using var payloadStream = new MemoryStream();
        Serializer.Serialize(payloadStream, payload);
        var payloadBytes = payloadStream.ToArray();

        Span<byte> headerBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.Slice(0, 4), TrayItemHeaderType);
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.Slice(4, 4), checked((uint)payloadBytes.Length));

        using var stream = File.Create(trayItemPath);
        stream.Write(headerBytes);
        stream.Write(payloadBytes, 0, payloadBytes.Length);
    }

    private static void WriteHouseholdBinary(string path, HouseholdData household)
    {
        using var stream = File.Create(path);
        Serializer.Serialize(stream, household);
    }

    private static void WritePlaceholderImage(string path)
    {
        File.WriteAllBytes(path, PlaceholderPngBytes);
    }

    private static TrayMetadata BuildTrayMetadata(
        ulong instanceId,
        SaveHouseholdItem household,
        SaveHouseholdExportRequest request)
    {
        var payload = new TrayMetadata
        {
            id = instanceId,
            type = ExchangeItemTypes.EXCHANGE_HOUSEHOLD,
            name = household.Name,
            description = household.Description,
            creator_id = request.CreatorId,
            creator_name = request.CreatorName,
            item_timestamp = checked((ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            metadata = new SpecificData
            {
                is_hidden = false,
                hh_metadata = new TrayHouseholdMetadata
                {
                    family_size = checked((uint)household.HouseholdSize),
                    pending_babies = 0
                }
            }
        };

        foreach (var member in household.Members)
        {
            payload.metadata.hh_metadata!.sim_data.Add(new TraySimMetadata
            {
                first_name = member.FirstName,
                last_name = member.LastName,
                id = member.SimId,
                gender = member.Gender,
                age = member.Age,
                species = member.Species,
                occult_types = member.OccultFlags
            });
        }

        return payload;
    }

    private static bool ValidateWrittenFiles(IReadOnlyCollection<string> writtenFiles, int expectedSgiCount)
    {
        if (writtenFiles.Count == 0)
        {
            return false;
        }

        var trayItemCount = writtenFiles.Count(path => path.EndsWith(".trayitem", StringComparison.OrdinalIgnoreCase));
        var householdBinaryCount = writtenFiles.Count(path => path.EndsWith(".householdbinary", StringComparison.OrdinalIgnoreCase));
        var hhiCount = writtenFiles.Count(path => path.EndsWith(".hhi", StringComparison.OrdinalIgnoreCase));
        var sgiCount = writtenFiles.Count(path => path.EndsWith(".sgi", StringComparison.OrdinalIgnoreCase));
        if (trayItemCount != 1 || householdBinaryCount != 1 || hhiCount != 2 || sgiCount != expectedSgiCount)
        {
            return false;
        }

        return writtenFiles.All(File.Exists);
    }

    private static ulong GenerateInstanceId()
    {
        Span<byte> buffer = stackalloc byte[8];
        ulong value;
        do
        {
            RandomNumberGenerator.Fill(buffer);
            value = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        }
        while (value == 0);

        return value;
    }

    private static string CreateUniqueExportDirectory(string exportRootPath, string householdName, ulong householdId)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(householdName) ? "Household" : householdName);
        var baseName = $"{safeName}_{householdId:X}_{timestamp}";
        var root = Path.GetFullPath(exportRootPath);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var directoryName = attempt == 0
                ? baseName
                : $"{baseName}_{attempt}";
            var fullPath = Path.Combine(root, directoryName);
            if (Directory.Exists(fullPath))
            {
                continue;
            }

            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        throw new IOException("Unable to create a unique export directory.");
    }

    private static string BuildFileName(uint type, ulong instanceId, string extension)
    {
        return $"0x{type:X8}!0x{instanceId:X16}{extension}";
    }

    private static uint BuildSimGlyphType(int slot)
    {
        return (uint)((slot << 4) | 0x3);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Household" : sanitized;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static SaveHouseholdExportResult Failed(string error)
    {
        return new SaveHouseholdExportResult
        {
            Succeeded = false,
            Error = error,
            Warnings = Array.Empty<string>(),
            WrittenFiles = Array.Empty<string>()
        };
    }
}
