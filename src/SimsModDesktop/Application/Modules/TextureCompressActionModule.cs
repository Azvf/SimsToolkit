using System.Text.Json.Nodes;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Infrastructure.TextureProcessing;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Modules;

public sealed class TextureCompressActionModule : IActionModule
{
    private static readonly IReadOnlyList<string> ActionPatchKeys =
    [
        "sourcePath",
        "outputPath",
        "targetWidth",
        "targetHeight",
        "hasAlphaHint",
        "generateMipMaps",
        "preferredFormat"
    ];

    private readonly ITextureCompressModuleState _panel;

    public TextureCompressActionModule(ITextureCompressModuleState panel)
    {
        _panel = panel;
    }

    public SimsAction Action => SimsAction.TextureCompress;
    public string ModuleKey => "texturecompress";
    public string DisplayName => "Texture Compress";
    public bool UsesSharedFileOps => false;
    public IReadOnlyCollection<string> SupportedActionPatchKeys => ActionPatchKeys;

    public void LoadFromSettings(AppSettings settings)
    {
        _panel.SourcePath = settings.TextureCompress.SourcePath;
        _panel.OutputPath = settings.TextureCompress.OutputPath;
        _panel.TargetWidthText = settings.TextureCompress.TargetWidthText;
        _panel.TargetHeightText = settings.TextureCompress.TargetHeightText;
        _panel.HasAlphaHint = settings.TextureCompress.HasAlphaHint;
        _panel.GenerateMipMaps = settings.TextureCompress.GenerateMipMaps;
        _panel.PreferredFormat = NormalizePreferredFormat(settings.TextureCompress.PreferredFormat);
    }

    public void SaveToSettings(AppSettings settings)
    {
        settings.TextureCompress.SourcePath = _panel.SourcePath;
        settings.TextureCompress.OutputPath = _panel.OutputPath;
        settings.TextureCompress.TargetWidthText = _panel.TargetWidthText;
        settings.TextureCompress.TargetHeightText = _panel.TargetHeightText;
        settings.TextureCompress.HasAlphaHint = _panel.HasAlphaHint;
        settings.TextureCompress.GenerateMipMaps = _panel.GenerateMipMaps;
        settings.TextureCompress.PreferredFormat = NormalizePreferredFormat(_panel.PreferredFormat);
    }

    public bool TryBuildPlan(GlobalExecutionOptions options, out ModuleExecutionPlan plan, out string error)
    {
        _ = options;
        plan = null!;
        error = string.Empty;

        var sourcePath = ModuleHelpers.ToNullIfWhiteSpace(_panel.SourcePath);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            error = "SourcePath is required for texture compression.";
            return false;
        }

        sourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourcePath))
        {
            error = "SourcePath does not exist for texture compression.";
            return false;
        }

        var outputPath = ResolveOutputPath(sourcePath, _panel.OutputPath);
        if (string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            error = "OutputPath must be different from SourcePath.";
            return false;
        }

        if (!TryParseOptionalDimension(_panel.TargetWidthText, "TargetWidth", out var targetWidth, out error) ||
            !TryParseOptionalDimension(_panel.TargetHeightText, "TargetHeight", out var targetHeight, out error))
        {
            return false;
        }

        if ((targetWidth.HasValue && !targetHeight.HasValue) || (!targetWidth.HasValue && targetHeight.HasValue))
        {
            error = "TargetWidth and TargetHeight must both be set, or both be left blank.";
            return false;
        }

        if (!TryParsePreferredFormat(_panel.PreferredFormat, out var preferredFormat, out error))
        {
            return false;
        }

        plan = new TextureCompressionExecutionPlan(new TextureCompressionFileRequest
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            TargetWidth = targetWidth,
            TargetHeight = targetHeight,
            HasAlphaHint = _panel.HasAlphaHint,
            GenerateMipMaps = _panel.GenerateMipMaps,
            PreferredFormat = preferredFormat,
            WhatIf = options.WhatIf
        });

        return true;
    }

    public bool TryApplyActionPatch(JsonObject patch, out string error)
    {
        error = string.Empty;

        if (!ModuleHelpers.TryGetString(patch, "sourcePath", out var hasSourcePath, out var sourcePath, out error) ||
            !ModuleHelpers.TryGetString(patch, "outputPath", out var hasOutputPath, out var outputPath, out error) ||
            !ModuleHelpers.TryGetString(patch, "preferredFormat", out var hasPreferredFormat, out var preferredFormat, out error) ||
            !ModuleHelpers.TryGetBoolean(patch, "hasAlphaHint", out var hasAlphaHint, out var hasAlphaValue, out error) ||
            !ModuleHelpers.TryGetBoolean(patch, "generateMipMaps", out var hasGenerateMipMaps, out var generateMipMaps, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetInt32(patch, "targetWidth", out var hasTargetWidth, out var targetWidth, out error) ||
            !ModuleHelpers.TryGetInt32(patch, "targetHeight", out var hasTargetHeight, out var targetHeight, out error))
        {
            return false;
        }

        if (hasSourcePath)
        {
            _panel.SourcePath = sourcePath ?? string.Empty;
        }

        if (hasOutputPath)
        {
            _panel.OutputPath = outputPath ?? string.Empty;
        }

        if (hasTargetWidth)
        {
            _panel.TargetWidthText = targetWidth.ToString();
        }

        if (hasTargetHeight)
        {
            _panel.TargetHeightText = targetHeight.ToString();
        }

        if (hasAlphaHint)
        {
            _panel.HasAlphaHint = hasAlphaValue;
        }

        if (hasGenerateMipMaps)
        {
            _panel.GenerateMipMaps = generateMipMaps;
        }

        if (hasPreferredFormat)
        {
            _panel.PreferredFormat = NormalizePreferredFormat(preferredFormat);
        }

        return true;
    }

    private static string ResolveOutputPath(string sourcePath, string? rawOutputPath)
    {
        var outputPath = ModuleHelpers.ToNullIfWhiteSpace(rawOutputPath);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        var directory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var extension = Path.GetExtension(sourcePath);
        var suffix = string.Equals(extension, ".dds", StringComparison.OrdinalIgnoreCase)
            ? ".compressed.dds"
            : ".dds";
        var fileName = Path.GetFileNameWithoutExtension(sourcePath) + suffix;
        return Path.Combine(directory, fileName);
    }

    private static bool TryParseOptionalDimension(string rawValue, string fieldName, out int? value, out string error)
    {
        error = string.Empty;
        value = null;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!int.TryParse(rawValue.Trim(), out var parsed) || parsed < 1 || parsed > 16384)
        {
            error = $"{fieldName} must be an integer between 1 and 16384.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParsePreferredFormat(string? rawValue, out TextureTargetFormat? value, out string error)
    {
        error = string.Empty;
        value = NormalizePreferredFormat(rawValue) switch
        {
            "Auto" => null,
            "BC1" => TextureTargetFormat.Bc1,
            "BC3" => TextureTargetFormat.Bc3,
            _ => null
        };

        if (value is null && !string.Equals(NormalizePreferredFormat(rawValue), "Auto", StringComparison.OrdinalIgnoreCase))
        {
            error = "PreferredFormat must be Auto, BC1, or BC3.";
            return false;
        }

        return true;
    }

    private static string NormalizePreferredFormat(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "BC1" => "BC1",
            "BC3" => "BC3",
            _ => "Auto"
        };
    }
}
