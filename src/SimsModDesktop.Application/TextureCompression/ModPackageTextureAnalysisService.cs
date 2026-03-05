using SimsModDesktop.PackageCore;
using System.Buffers.Binary;
using System.Buffers;

namespace SimsModDesktop.Application.TextureCompression;

public sealed class ModPackageTextureAnalysisService : IModPackageTextureAnalysisService
{
    private const uint DdsTypeId = Sims4ResourceTypeRegistry.Dds;
    private const uint DstTypeId = Sims4ResourceTypeRegistry.Dst;
    private const uint Rle2TypeId = Sims4ResourceTypeRegistry.Rle2;
    private const uint DdsMagic = 0x20534444;
    private const uint Dxt1FourCc = 0x31545844;
    private const uint Dxt5FourCc = 0x35545844;
    private const uint Dx10FourCc = 0x30315844;
    private const uint DdpfAlphaPixels = 0x1;
    private const uint DdpfFourCc = 0x4;
    private const uint DdpfRgb = 0x40;

    private readonly IModPackageTextureAnalysisStore _store;
    private readonly IDbpfResourceReader _resourceReader;

    public ModPackageTextureAnalysisService(
        IModPackageTextureAnalysisStore store,
        IDbpfResourceReader? resourceReader = null)
    {
        _store = store;
        _resourceReader = resourceReader ?? new DbpfResourceReader();
    }

    public async Task<ModPackageTextureSummary?> TryGetCachedAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fileInfo = ValidatePackagePath(packagePath);
        var cached = await _store.TryGetAsync(fileInfo.FullName, fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, cancellationToken);
        return cached?.Summary;
    }

    public async Task<ModPackageTextureSummary> AnalyzeAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        var result = await AnalyzeResultAsync(packagePath, cancellationToken);
        return result.Summary;
    }

    public async Task<ModPackageTextureAnalysisResult?> TryGetCachedResultAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fileInfo = ValidatePackagePath(packagePath);
        return await _store.TryGetAsync(fileInfo.FullName, fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, cancellationToken);
    }

    public async Task<ModPackageTextureAnalysisResult> AnalyzeResultAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fileInfo = ValidatePackagePath(packagePath);
        var fullPath = fileInfo.FullName;
        var cached = await _store.TryGetAsync(fullPath, fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var index = DbpfPackageIndexReader.ReadPackageIndex(fullPath);
        var ddsCount = 0;
        var unsupportedTextureCount = 0;
        long totalTextureBytes = 0;
        var candidates = new List<ModPackageTextureCandidate>();
        var payload = new ArrayBufferWriter<byte>();

        using var session = _resourceReader.OpenSession(fullPath);

        foreach (var entry in index.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDeleted)
            {
                continue;
            }

            switch (entry.Type)
            {
                case DdsTypeId:
                    ddsCount++;
                    totalTextureBytes += entry.CompressedSize;
                    if (TryBuildDdsCandidate(session, entry, payload, out var ddsCandidate))
                    {
                        candidates.Add(ddsCandidate);
                    }
                    break;
                case DstTypeId:
                case Rle2TypeId:
                    unsupportedTextureCount++;
                    totalTextureBytes += entry.CompressedSize;
                    candidates.Add(new ModPackageTextureCandidate
                    {
                        ResourceKeyText = FormatResourceKey(entry),
                        ContainerKind = entry.Type == DstTypeId ? "DST" : "RLE2",
                        Format = "Unsupported",
                        Width = 0,
                        Height = 0,
                        MipMapCount = 0,
                        Editable = false,
                        SuggestedAction = "Skip",
                        Notes = "Sims-specific texture format; keep as-is in the first edit pass.",
                        SizeBytes = entry.CompressedSize
                    });
                    break;
                default:
                    if (entry.Type == Sims4ResourceTypeRegistry.DdsUncompressed)
                    {
                        ddsCount++;
                        totalTextureBytes += entry.CompressedSize;
                        if (TryBuildDdsCandidate(session, entry, payload, out var uncompressedDdsCandidate))
                        {
                            candidates.Add(uncompressedDdsCandidate);
                        }
                    }
                    break;
            }
        }

        var summary = new ModPackageTextureSummary
        {
            PackagePath = fullPath,
            FileLength = fileInfo.Length,
            LastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
            TextureResourceCount = ddsCount + unsupportedTextureCount,
            DdsCount = ddsCount,
            PngCount = 0,
            UnsupportedTextureCount = unsupportedTextureCount,
            EditableTextureCount = ddsCount,
            TotalTextureBytes = totalTextureBytes,
            LastAnalyzedLocal = DateTime.Now
        };

        var result = new ModPackageTextureAnalysisResult
        {
            Summary = summary,
            Candidates = candidates
                .OrderByDescending(candidate => candidate.Editable)
                .ThenByDescending(candidate => candidate.SizeBytes)
                .ToArray()
        };

        await _store.SaveAsync(result, cancellationToken);
        return result;
    }

    private static bool TryBuildDdsCandidate(
        DbpfPackageReadSession session,
        DbpfIndexEntry entry,
        ArrayBufferWriter<byte> payload,
        out ModPackageTextureCandidate candidate)
    {
        candidate = null!;
        payload.Clear();
        if (!session.TryReadInto(entry, payload, out _))
        {
            return false;
        }

        var bytes = payload.WrittenSpan;
        if (bytes.Length < 128 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4)) != DdsMagic)
        {
            return false;
        }

        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12, 4));
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(16, 4));
        var mipMapCount = Math.Max(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(28, 4)));
        var pixelFormatFlags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(80, 4));
        var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(84, 4));
        var rgbBitCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(88, 4));
        var (format, editable, suggestedAction, notes) = ResolveDdsClassification(
            bytes,
            width,
            height,
            pixelFormatFlags,
            fourCc,
            rgbBitCount);

        candidate = new ModPackageTextureCandidate
        {
            ResourceKeyText = FormatResourceKey(entry),
            ContainerKind = "DDS",
            Format = format,
            Width = width,
            Height = height,
            MipMapCount = mipMapCount,
            Editable = editable,
            SuggestedAction = suggestedAction,
            Notes = notes,
            SizeBytes = entry.CompressedSize
        };
        return true;
    }

    private static (string Format, bool Editable, string SuggestedAction, string Notes) ResolveDdsClassification(
        ReadOnlySpan<byte> bytes,
        int width,
        int height,
        uint pixelFormatFlags,
        uint fourCc,
        int rgbBitCount)
    {
        if ((pixelFormatFlags & DdpfFourCc) != 0)
        {
            if (fourCc == Dxt1FourCc)
            {
                return MaxDimension(width, height) >= 4096
                    ? ("DXT1", true, "Downscale2048+ReencodeBC1", "Already GPU-native; resize only if oversized.")
                    : ("DXT1", true, "Keep", "Already GPU-native BC1.");
            }

            if (fourCc == Dxt5FourCc)
            {
                return MaxDimension(width, height) >= 4096
                    ? ("DXT5", true, "Downscale2048+ReencodeBC3", "Already GPU-native; resize only if oversized.")
                    : ("DXT5", true, "Keep", "Already GPU-native BC3.");
            }

            if (fourCc == Dx10FourCc && bytes.Length >= 132)
            {
                var dxgiFormat = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(128, 4));
                if (dxgiFormat is 98 or 99)
                {
                    return ("BC7", false, "Skip", "BC7 detected; keep as-is for the first safe edit pass.");
                }

                return ($"DX10({dxgiFormat})", false, "Skip", "DX10 DDS variant is not enabled for automatic editing yet.");
            }

            return ($"FourCC(0x{fourCc:X8})", false, "Skip", "Unsupported DDS compression format.");
        }

        if ((pixelFormatFlags & DdpfRgb) != 0)
        {
            var hasAlpha = (pixelFormatFlags & DdpfAlphaPixels) != 0 || rgbBitCount >= 32;
            if (rgbBitCount >= 32)
            {
                return MaxDimension(width, height) >= 4096
                    ? ("RGBA32", true, "ConvertToBC3+Downscale2048", "Uncompressed DDS with alpha.")
                    : ("RGBA32", true, "ConvertToBC3", "Uncompressed DDS with alpha.");
            }

            if (rgbBitCount >= 24)
            {
                return MaxDimension(width, height) >= 4096
                    ? ("RGB24", true, "ConvertToBC1+Downscale2048", "Uncompressed DDS without alpha.")
                    : ("RGB24", true, hasAlpha ? "ConvertToBC3" : "ConvertToBC1", "Uncompressed DDS.");
            }
        }

        return ("Unknown", false, "Skip", "DDS header could not be classified safely.");
    }

    private static string FormatResourceKey(DbpfIndexEntry entry)
    {
        return $"{entry.Type:X8}:{entry.Group:X8}:{entry.Instance:X16}";
    }

    private static int MaxDimension(int width, int height) => Math.Max(width, height);

    private static FileInfo ValidatePackagePath(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var fullPath = Path.GetFullPath(packagePath.Trim());
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Package file was not found.", fullPath);
        }

        if (!string.Equals(fileInfo.Extension, ".package", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Texture analysis is only available for .package files.");
        }

        return fileInfo;
    }
}
