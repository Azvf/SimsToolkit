using System.Buffers.Binary;
using System.Buffers;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SimsModDesktop.Application.TextureProcessing;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Infrastructure.TextureCompression;

public sealed class ModPackageTextureEditService : IModPackageTextureEditService
{
    private const int HeaderLength = 96;
    private const ushort CompressionNone = 0;
    private const ushort CompressionDeleted = 65504;

    private readonly ITextureDecodeService _decoder;
    private readonly ITextureTranscodePipeline _pipeline;
    private readonly IModPackageTextureEditStore _store;
    private readonly IDbpfResourceReader _resourceReader;
    private readonly ILogger<ModPackageTextureEditService> _logger;

    public ModPackageTextureEditService(
        ITextureDecodeService decoder,
        ITextureTranscodePipeline pipeline,
        IModPackageTextureEditStore store,
        IDbpfResourceReader? resourceReader = null,
        ILogger<ModPackageTextureEditService>? logger = null)
    {
        _decoder = decoder;
        _pipeline = pipeline;
        _store = store;
        _resourceReader = resourceReader ?? new DbpfResourceReader();
        _logger = logger ?? NullLogger<ModPackageTextureEditService>.Instance;
    }

    public async Task<ModPackageTexturePreviewResult> PreviewAsync(
        string packagePath,
        ModPackageTextureCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(candidate);

        if (!candidate.Editable)
        {
            return new ModPackageTexturePreviewResult
            {
                Success = false,
                Error = "This texture is not enabled for in-app preview."
            };
        }

        if (!TryReadResourceBytes(packagePath, candidate.ResourceKeyText, out var bytes, out _, out var readError))
        {
            return new ModPackageTexturePreviewResult
            {
                Success = false,
                Error = readError
            };
        }

        if (!_decoder.TryDecode(ToContainerKind(candidate.ContainerKind), bytes, out var pixelBuffer, out var decodeError))
        {
            return new ModPackageTexturePreviewResult
            {
                Success = false,
                Error = decodeError
            };
        }

        using var image = Image.LoadPixelData<Rgba32>(pixelBuffer.PixelBytes, pixelBuffer.Width, pixelBuffer.Height);
        using var output = new MemoryStream();
        await image.SaveAsPngAsync(output, cancellationToken).ConfigureAwait(false);
        return new ModPackageTexturePreviewResult
        {
            Success = true,
            PngBytes = output.ToArray(),
            Width = pixelBuffer.Width,
            Height = pixelBuffer.Height,
            Format = candidate.Format
        };
    }

    public async Task<ModPackageTextureEditExecutionResult> ApplySuggestedEditAsync(
        string packagePath,
        ModPackageTextureCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(candidate);

        if (!candidate.Editable)
        {
            return Failure(candidate.ResourceKeyText, "This texture is not enabled for in-app editing.");
        }

        if (!ShouldEdit(candidate))
        {
            return Failure(candidate.ResourceKeyText, "No in-app edit is needed for the current suggestion.");
        }

        if (!TryReadResourceBytes(packagePath, candidate.ResourceKeyText, out var sourceBytes, out _, out var readError))
        {
            return Failure(candidate.ResourceKeyText, readError);
        }

        var plan = BuildPlan(candidate);
        var request = new TextureTranscodeRequest
        {
            Source = new TextureSourceDescriptor
            {
                ResourceKey = ParseResourceKey(candidate.ResourceKeyText),
                ContainerKind = ToContainerKind(candidate.ContainerKind),
                SourcePixelFormat = ToPixelFormatKind(candidate.Format),
                Width = candidate.Width,
                Height = candidate.Height,
                HasAlpha = plan.TargetFormat == TextureTargetFormat.Bc3,
                MipMapCount = Math.Max(1, candidate.MipMapCount)
            },
            SourceBytes = sourceBytes,
            TargetFormat = plan.TargetFormat,
            TargetWidth = plan.TargetWidth,
            TargetHeight = plan.TargetHeight,
            GenerateMipMaps = candidate.MipMapCount > 1
        };

        var transcode = _pipeline.Transcode(request);
        if (!transcode.Success || transcode.EncodedBytes.Length == 0)
        {
            return Failure(candidate.ResourceKeyText, transcode.Error ?? "Texture transcode failed.");
        }

        ReplaceResource(packagePath, candidate.ResourceKeyText, transcode.EncodedBytes);

        var editId = Guid.NewGuid().ToString("N");
        await _store.SaveAsync(
            new ModPackageTextureEditRecord
            {
                EditId = editId,
                PackagePath = Path.GetFullPath(packagePath),
                ResourceKeyText = candidate.ResourceKeyText,
                RecordKind = "Apply",
                AppliedAction = candidate.SuggestedAction,
                OriginalBytes = sourceBytes,
                ReplacementBytes = transcode.EncodedBytes,
                AppliedUtcTicks = DateTime.UtcNow.Ticks,
                Notes = $"{candidate.Format} -> {plan.TargetFormat} ({transcode.OutputWidth}x{transcode.OutputHeight})"
            },
            cancellationToken).ConfigureAwait(false);

        return new ModPackageTextureEditExecutionResult
        {
            Success = true,
            ResourceKeyText = candidate.ResourceKeyText,
            EditId = editId,
            StatusText = "Texture resource replaced. The rollback payload is stored in the local database."
        };
    }

    public async Task<ModPackageTextureEditExecutionResult> ApplyImportedTextureAsync(
        string packagePath,
        ModPackageTextureCandidate candidate,
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (!candidate.Editable)
        {
            return Failure(candidate.ResourceKeyText, "This texture is not enabled for in-app editing.");
        }

        var fullSourcePath = Path.GetFullPath(sourceFilePath.Trim());
        if (!File.Exists(fullSourcePath))
        {
            return Failure(candidate.ResourceKeyText, "The imported texture file was not found.");
        }

        if (!TryReadResourceBytes(packagePath, candidate.ResourceKeyText, out var originalBytes, out _, out var readError))
        {
            return Failure(candidate.ResourceKeyText, readError);
        }

        var importedBytes = await File.ReadAllBytesAsync(fullSourcePath, cancellationToken).ConfigureAwait(false);
        var plan = BuildPlan(candidate);
        var request = new TextureTranscodeRequest
        {
            Source = new TextureSourceDescriptor
            {
                ResourceKey = ParseResourceKey(candidate.ResourceKeyText),
                ContainerKind = ResolveContainerKindFromFilePath(fullSourcePath),
                SourcePixelFormat = TexturePixelFormatKind.Unknown,
                Width = candidate.Width,
                Height = candidate.Height,
                HasAlpha = plan.TargetFormat == TextureTargetFormat.Bc3,
                MipMapCount = Math.Max(1, candidate.MipMapCount)
            },
            SourceBytes = importedBytes,
            TargetFormat = plan.TargetFormat,
            TargetWidth = plan.TargetWidth,
            TargetHeight = plan.TargetHeight,
            GenerateMipMaps = candidate.MipMapCount > 1
        };

        var transcode = _pipeline.Transcode(request);
        if (!transcode.Success || transcode.EncodedBytes.Length == 0)
        {
            return Failure(candidate.ResourceKeyText, transcode.Error ?? "Imported texture transcode failed.");
        }

        ReplaceResource(packagePath, candidate.ResourceKeyText, transcode.EncodedBytes);

        var editId = Guid.NewGuid().ToString("N");
        await _store.SaveAsync(
            new ModPackageTextureEditRecord
            {
                EditId = editId,
                PackagePath = Path.GetFullPath(packagePath),
                ResourceKeyText = candidate.ResourceKeyText,
                RecordKind = "Apply",
                AppliedAction = $"Import:{Path.GetFileName(fullSourcePath)}",
                OriginalBytes = originalBytes,
                ReplacementBytes = transcode.EncodedBytes,
                AppliedUtcTicks = DateTime.UtcNow.Ticks,
                Notes = $"Imported {Path.GetFileName(fullSourcePath)} -> {plan.TargetFormat} ({transcode.OutputWidth}x{transcode.OutputHeight})"
            },
            cancellationToken).ConfigureAwait(false);

        return new ModPackageTextureEditExecutionResult
        {
            Success = true,
            ResourceKeyText = candidate.ResourceKeyText,
            EditId = editId,
            StatusText = "Imported texture applied. The previous resource payload is stored in the local database."
        };
    }

    public async Task<ModPackageTextureEditExecutionResult> RollbackLatestAsync(
        string packagePath,
        string resourceKeyText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var latestEdit = await _store.TryGetLatestActiveEditAsync(packagePath, resourceKeyText, cancellationToken).ConfigureAwait(false);
        if (latestEdit is null)
        {
            return Failure(resourceKeyText, "No active in-app texture edit was found for rollback.");
        }

        if (!TryReadResourceBytes(packagePath, resourceKeyText, out var currentBytes, out _, out var readError))
        {
            return Failure(resourceKeyText, readError);
        }

        ReplaceResource(packagePath, resourceKeyText, latestEdit.OriginalBytes);

        var rollbackId = Guid.NewGuid().ToString("N");
        await _store.SaveAsync(
            new ModPackageTextureEditRecord
            {
                EditId = rollbackId,
                PackagePath = Path.GetFullPath(packagePath),
                ResourceKeyText = resourceKeyText,
                RecordKind = "Rollback",
                AppliedAction = "Rollback",
                OriginalBytes = currentBytes,
                ReplacementBytes = latestEdit.OriginalBytes,
                AppliedUtcTicks = DateTime.UtcNow.Ticks,
                TargetEditId = latestEdit.EditId,
                Notes = $"Rollback of {latestEdit.EditId}"
            },
            cancellationToken).ConfigureAwait(false);
        await _store.MarkRolledBackAsync(latestEdit.EditId, cancellationToken).ConfigureAwait(false);

        return new ModPackageTextureEditExecutionResult
        {
            Success = true,
            ResourceKeyText = resourceKeyText,
            EditId = rollbackId,
            StatusText = "Texture resource restored from the local database rollback record."
        };
    }

    public Task<IReadOnlyList<ModPackageTextureEditRecord>> GetHistoryAsync(
        string packagePath,
        string resourceKeyText,
        int maxCount = 10,
        CancellationToken cancellationToken = default)
    {
        return _store.GetHistoryAsync(packagePath, resourceKeyText, maxCount, cancellationToken);
    }

    private static bool ShouldEdit(ModPackageTextureCandidate candidate)
    {
        return !string.Equals(candidate.SuggestedAction, "Keep", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(candidate.SuggestedAction, "Skip", StringComparison.OrdinalIgnoreCase);
    }

    private static (TextureTargetFormat TargetFormat, int TargetWidth, int TargetHeight) BuildPlan(ModPackageTextureCandidate candidate)
    {
        var action = candidate.SuggestedAction;
        var targetFormat = action.Contains("BC3", StringComparison.OrdinalIgnoreCase)
            ? TextureTargetFormat.Bc3
            : TextureTargetFormat.Bc1;

        var targetWidth = candidate.Width;
        var targetHeight = candidate.Height;

        var downscaleIndex = action.IndexOf("Downscale", StringComparison.OrdinalIgnoreCase);
        if (downscaleIndex >= 0)
        {
            var digits = new string(action
                .Skip(downscaleIndex + "Downscale".Length)
                .TakeWhile(char.IsDigit)
                .ToArray());

            if (int.TryParse(digits, out var maxDimension) && maxDimension > 0)
            {
                (targetWidth, targetHeight) = ScaleToFit(candidate.Width, candidate.Height, maxDimension);
            }
        }

        if (targetWidth < 4 || targetHeight < 4)
        {
            targetWidth = Math.Max(4, targetWidth);
            targetHeight = Math.Max(4, targetHeight);
        }

        targetWidth -= targetWidth % 4;
        targetHeight -= targetHeight % 4;
        targetWidth = Math.Max(4, targetWidth);
        targetHeight = Math.Max(4, targetHeight);

        return (targetFormat, targetWidth, targetHeight);
    }

    private static (int Width, int Height) ScaleToFit(int width, int height, int maxDimension)
    {
        if (width <= maxDimension && height <= maxDimension)
        {
            return (width, height);
        }

        var scale = Math.Min(maxDimension / (double)width, maxDimension / (double)height);
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        return (targetWidth, targetHeight);
    }

    private bool TryReadResourceBytes(
        string packagePath,
        string resourceKeyText,
        out byte[] bytes,
        out DbpfIndexEntry entry,
        out string error)
    {
        bytes = Array.Empty<byte>();
        entry = default;
        error = string.Empty;

        var index = DbpfPackageIndexReader.ReadPackageIndex(packagePath);
        var resourceKey = ParseResourceKey(resourceKeyText);
        var found = false;
        DbpfIndexEntry matchedEntry = default;
        foreach (var candidate in index.Entries)
        {
            if (candidate.IsDeleted ||
                candidate.Type != resourceKey.Type ||
                candidate.Group != resourceKey.Group ||
                candidate.Instance != resourceKey.Instance)
            {
                continue;
            }

            matchedEntry = candidate;
            found = true;
            break;
        }

        if (!found)
        {
            error = "Texture resource was not found in the package.";
            return false;
        }

        using var session = _resourceReader.OpenSession(packagePath);
        var payload = new ArrayBufferWriter<byte>();
        if (!session.TryReadInto(matchedEntry, payload, out var readError))
        {
            error = readError ?? "Texture resource could not be read.";
            _logger.LogDebug(
                "resource.read.pooled domain={Domain} success={Success} packagePath={PackagePath} resourceKey={ResourceKey} error={Error}",
                "texture-edit",
                false,
                packagePath,
                resourceKeyText,
                error);
            return false;
        }

        _logger.LogDebug(
            "resource.read.pooled domain={Domain} success={Success} packagePath={PackagePath} resourceKey={ResourceKey} bytesRead={BytesRead}",
            "texture-edit",
            true,
            packagePath,
            resourceKeyText,
            payload.WrittenCount);
        bytes = payload.WrittenSpan.ToArray();
        entry = matchedEntry;
        return true;
    }

    private void ReplaceResource(
        string packagePath,
        string resourceKeyText,
        byte[] replacementBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKeyText);
        ArgumentNullException.ThrowIfNull(replacementBytes);

        var fullPath = Path.GetFullPath(packagePath);
        var index = DbpfPackageIndexReader.ReadPackageIndex(fullPath);
        var resourceKey = ParseResourceKey(resourceKeyText);

        var headerBytes = new byte[HeaderLength];
        using (var headerStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            FillExactly(headerStream, headerBytes);
        }

        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";

        using (var sourceStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            output.SetLength(0);
            output.Write(headerBytes, 0, headerBytes.Length);

            var rewrittenEntries = new DbpfIndexEntry[index.Entries.Length];

            for (var i = 0; i < index.Entries.Length; i++)
            {
                var entry = index.Entries[i];
                if (entry.IsDeleted)
                {
                    rewrittenEntries[i] = entry with
                    {
                        DataOffset = 0,
                        CompressedSize = 0,
                        UncompressedSize = 0,
                        CompressionType = CompressionDeleted,
                        IsDeleted = true
                    };
                    continue;
                }

                byte[] payload;
                ushort compressionType;
                var uncompressedSize = entry.UncompressedSize;
                if (entry.Type == resourceKey.Type &&
                    entry.Group == resourceKey.Group &&
                    entry.Instance == resourceKey.Instance)
                {
                    payload = replacementBytes;
                    compressionType = CompressionNone;
                    uncompressedSize = replacementBytes.Length;
                }
                else
                {
                    payload = ReadStoredPayload(sourceStream, entry);
                    compressionType = entry.CompressionType;
                }

                var dataOffset = checked((int)output.Position);
                output.Write(payload, 0, payload.Length);
                rewrittenEntries[i] = entry with
                {
                    DataOffset = dataOffset,
                    CompressedSize = payload.Length,
                    UncompressedSize = uncompressedSize,
                    CompressionType = compressionType,
                    IsDeleted = false
                };
            }

            if (!rewrittenEntries.Any(entry =>
                    !entry.IsDeleted &&
                    entry.Type == resourceKey.Type &&
                    entry.Group == resourceKey.Group &&
                    entry.Instance == resourceKey.Instance))
            {
                throw new InvalidOperationException("Texture resource was not found in the package.");
            }

            var indexPosition = output.Position;
            Span<byte> flagsBytes = stackalloc byte[4];
            flagsBytes.Clear();
            output.Write(flagsBytes);

            foreach (var entry in rewrittenEntries)
            {
                Span<byte> entryBytes = new byte[32];
                entryBytes.Clear();
                BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.Slice(0, 4), entry.Type);
                BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.Slice(4, 4), entry.Group);
                BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.Slice(8, 4), (uint)(entry.Instance >> 32));
                BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.Slice(12, 4), (uint)(entry.Instance & 0xFFFFFFFF));
                BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.Slice(16, 4), (uint)entry.DataOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.Slice(20, 4), (uint)entry.CompressedSize);
                BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.Slice(24, 4), (uint)entry.UncompressedSize);
                BinaryPrimitives.WriteUInt16LittleEndian(entryBytes.Slice(28, 2), entry.CompressionType);
                output.Write(entryBytes);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(36, 4), (uint)rewrittenEntries.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(40, 4), indexPosition <= uint.MaxValue ? (uint)indexPosition : 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(44, 4), checked((uint)(4 + (rewrittenEntries.Length * 32))));
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(48, 4), 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(52, 4), 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(56, 4), 0u);
            BinaryPrimitives.WriteUInt64LittleEndian(headerBytes.AsSpan(64, 8), checked((ulong)indexPosition));

            output.Position = 0;
            output.Write(headerBytes, 0, headerBytes.Length);
            output.Flush(flushToDisk: true);
        }

        File.Move(tempPath, fullPath, overwrite: true);
    }

    private static byte[] ReadStoredPayload(Stream sourceStream, DbpfIndexEntry entry)
    {
        if (entry.CompressedSize < 0)
        {
            throw new InvalidOperationException("Package entry has an invalid stored size.");
        }

        var payload = new byte[entry.CompressedSize];
        sourceStream.Seek(entry.DataOffset, SeekOrigin.Begin);
        FillExactly(sourceStream, payload);
        return payload;
    }

    private static DbpfResourceKey ParseResourceKey(string resourceKeyText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKeyText);
        var parts = resourceKeyText.Trim().Split(':');
        if (parts.Length != 3)
        {
            throw new InvalidOperationException("Invalid texture resource key.");
        }

        if (!uint.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out var type) ||
            !uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var group) ||
            !ulong.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out var instance))
        {
            throw new InvalidOperationException("Invalid texture resource key.");
        }

        return new DbpfResourceKey(type, group, instance);
    }

    private static TextureContainerKind ToContainerKind(string containerKind)
    {
        return containerKind.ToUpperInvariant() switch
        {
            "DDS" => TextureContainerKind.Dds,
            "TGA" => TextureContainerKind.Tga,
            _ => TextureContainerKind.Png
        };
    }

    private static TextureContainerKind ResolveContainerKindFromFilePath(string sourceFilePath)
    {
        var extension = Path.GetExtension(sourceFilePath);
        return extension.ToLowerInvariant() switch
        {
            ".dds" => TextureContainerKind.Dds,
            ".tga" => TextureContainerKind.Tga,
            ".png" => TextureContainerKind.Png,
            _ => throw new InvalidOperationException("Only PNG, DDS, or TGA files can be imported for texture replacement.")
        };
    }

    private static TexturePixelFormatKind ToPixelFormatKind(string format)
    {
        if (format.Contains("DXT1", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("BC1", StringComparison.OrdinalIgnoreCase))
        {
            return TexturePixelFormatKind.Bc1;
        }

        if (format.Contains("DXT5", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("BC3", StringComparison.OrdinalIgnoreCase))
        {
            return TexturePixelFormatKind.Bc3;
        }

        if (format.Contains("BC7", StringComparison.OrdinalIgnoreCase))
        {
            return TexturePixelFormatKind.Bc7;
        }

        if (format.Contains("RGBA", StringComparison.OrdinalIgnoreCase))
        {
            return TexturePixelFormatKind.Rgba32;
        }

        if (format.Contains("RGB", StringComparison.OrdinalIgnoreCase))
        {
            return TexturePixelFormatKind.Rgb24;
        }

        return TexturePixelFormatKind.Unknown;
    }

    private static ModPackageTextureEditExecutionResult Failure(string resourceKeyText, string message)
    {
        return new ModPackageTextureEditExecutionResult
        {
            Success = false,
            ResourceKeyText = resourceKeyText,
            Error = message,
            StatusText = message
        };
    }

    private static void FillExactly(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }

            total += read;
        }
    }
}
