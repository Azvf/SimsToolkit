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
    private const int EncodedImageHeaderSize = 24;
    private const int MaxHouseholdMembers = 8;
    private static ReadOnlySpan<byte> EncodedImageXorKey => [0x41, 0x25, 0xE6, 0xCD, 0x47, 0xBA, 0xB2, 0x1A];
    private static readonly byte[] PlaceholderJpegBytes = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAQABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDj/uf7O38MY/75xjZ/s42fwbP3B9z/AGdv4Yx/3zjGz/Zxs/g2fuD7n+zt/DGP++cY2f7ONn8Gz9wfc/2dv4Yx/wB84xs/2cbP4Nn7j+jz8CP/2Q==");
    private static readonly byte[] PlaceholderEncodedImageBytes = BuildEncodedPlaceholderImageBytes();

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

            var instanceId = request.InstanceIdOverride.GetValueOrDefault();
            if (instanceId == 0)
            {
                instanceId = GenerateInstanceId();
            }

            var hasOutputOverride = !string.IsNullOrWhiteSpace(request.OutputDirectoryOverride);
            var exportDirectory = hasOutputOverride
                ? PrepareOutputDirectory(request.OutputDirectoryOverride!)
                : CreateUniqueExportDirectory(request.ExportRootPath, household.Name, household.HouseholdId);
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
                    CleanupFailedExport(exportDirectory, writtenFiles, hasOutputOverride);
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
                CleanupFailedExport(exportDirectory, writtenFiles, hasOutputOverride);
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
        File.WriteAllBytes(path, PlaceholderEncodedImageBytes);
    }

    private static TrayMetadata BuildTrayMetadata(
        ulong instanceId,
        SaveHouseholdItem household,
        SaveHouseholdExportRequest request)
    {
        var effectiveName = string.IsNullOrWhiteSpace(request.MetadataNameOverride)
            ? household.Name
            : request.MetadataNameOverride.Trim();
        var effectiveDescription = request.MetadataDescriptionOverride ?? household.Description;

        var payload = new TrayMetadata
        {
            id = instanceId,
            type = ExchangeItemTypes.EXCHANGE_HOUSEHOLD,
            name = effectiveName,
            description = effectiveDescription,
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

    private static byte[] BuildEncodedPlaceholderImageBytes()
    {
        var payload = new byte[PlaceholderJpegBytes.Length];
        PlaceholderJpegBytes.CopyTo(payload, 0);

        var encoded = new byte[EncodedImageHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(0, 4), checked((uint)payload.Length));

        var key = EncodedImageXorKey;
        for (var i = 0; i < payload.Length; i++)
        {
            encoded[EncodedImageHeaderSize + i] = (byte)(payload[i] ^ key[i % key.Length]);
        }

        return encoded;
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

    private static string PrepareOutputDirectory(string outputDirectory)
    {
        var fullPath = Path.GetFullPath(outputDirectory.Trim());
        Directory.CreateDirectory(fullPath);
        return fullPath;
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

    private static void SafeDeleteFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    private static void CleanupFailedExport(string exportDirectory, IReadOnlyCollection<string> writtenFiles, bool usedOutputOverride)
    {
        if (usedOutputOverride)
        {
            SafeDeleteFiles(writtenFiles);
            return;
        }

        SafeDeleteDirectory(exportDirectory);
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
