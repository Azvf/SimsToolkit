using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class TrayMetadataService : ITrayMetadataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ToolPathEnvironmentVariables =
    {
        "SIMS_TOOLKIT_S4TI_PATH",
        "S4TI_PATH"
    };

    private static readonly string[] DefaultToolPathCandidates =
    {
        @"D:\Sims Mods\Tools\S4TI_250831",
        @"D:\Sims Mods\Tools"
    };

    private readonly object _gate = new();

    private bool _toolPathResolved;
    private string _cachedToolPath = string.Empty;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trayItemPaths);

        if (!OperatingSystem.IsWindows() || trayItemPaths.Count == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<string, TrayMetadataResult>>(
                new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase));
        }

        return Task.Run(
            () => GetMetadataCore(trayItemPaths, cancellationToken),
            cancellationToken);
    }

    private IReadOnlyDictionary<string, TrayMetadataResult> GetMetadataCore(
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken)
    {
        var normalizedPaths = trayItemPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .ToList();
        if (normalizedPaths.Count == 0)
        {
            return new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
        }

        var results = new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
        var misses = new List<string>();

        lock (_gate)
        {
            foreach (var path in normalizedPaths)
            {
                var file = new FileInfo(path);
                if (_cache.TryGetValue(path, out var cached) &&
                    cached.Length == file.Length &&
                    cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
                {
                    results[path] = cached.Value;
                    continue;
                }

                misses.Add(path);
            }
        }

        if (misses.Count == 0)
        {
            return results;
        }

        var toolPath = ResolveS4TiToolPath();
        var powerShellExecutable = ResolveWindowsPowerShellExecutable();
        if (string.IsNullOrWhiteSpace(toolPath) || string.IsNullOrWhiteSpace(powerShellExecutable))
        {
            return results;
        }

        var loaded = ExecuteBatch(toolPath, powerShellExecutable, misses, cancellationToken);
        if (loaded.Count == 0)
        {
            return results;
        }

        lock (_gate)
        {
            foreach (var pair in loaded)
            {
                var file = new FileInfo(pair.Key);
                _cache[pair.Key] = new CacheEntry
                {
                    Length = file.Exists ? file.Length : 0,
                    LastWriteTimeUtc = file.Exists ? file.LastWriteTimeUtc : DateTime.MinValue,
                    Value = pair.Value
                };
                results[pair.Key] = pair.Value;
            }
        }

        return results;
    }

    private static IReadOnlyDictionary<string, TrayMetadataResult> ExecuteBatch(
        string toolPath,
        string powerShellExecutable,
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);

        string tempDirectory = string.Empty;
        string inputPath = string.Empty;
        string outputPath = string.Empty;

        try
        {
            tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "SimsModDesktop",
                "TrayMetadata",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            inputPath = Path.Combine(tempDirectory, "input.json");
            outputPath = Path.Combine(tempDirectory, "output.json");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(trayItemPaths, JsonOptions), Encoding.UTF8);

            var encodedCommand = Convert.ToBase64String(
                Encoding.Unicode.GetBytes(BuildExtractionScript(toolPath, inputPath, outputPath)));

            var startInfo = new ProcessStartInfo
            {
                FileName = powerShellExecutable,
                WorkingDirectory = toolPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Sta");
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(encodedCommand);

            using var process = new Process
            {
                StartInfo = startInfo
            };

            cancellationToken.ThrowIfCancellationRequested();

            if (!process.Start())
            {
                return results;
            }

            using var cancellationRegistration = cancellationToken.Register(() => TryKillProcess(process));
            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                return results;
            }

            var json = File.ReadAllText(outputPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return results;
            }

            using var document = JsonDocument.Parse(json);
            switch (document.RootElement.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        if (TryParseResult(element, out var result))
                        {
                            results[result.TrayItemPath] = result;
                        }
                    }

                    break;
                case JsonValueKind.Object:
                    if (TryParseResult(document.RootElement, out var singleResult))
                    {
                        results[singleResult.TrayItemPath] = singleResult;
                    }

                    break;
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteFile(inputPath);
            TryDeleteFile(outputPath);
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static bool TryParseResult(JsonElement element, out TrayMetadataResult result)
    {
        result = null!;

        var trayItemPath = ReadString(element, "trayItemPath");
        if (string.IsNullOrWhiteSpace(trayItemPath))
        {
            return false;
        }

        var members = new List<TrayMemberDisplayMetadata>();
        if (TryGetPropertyIgnoreCase(element, "members", out var membersElement) &&
            membersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var memberElement in membersElement.EnumerateArray())
            {
                members.Add(new TrayMemberDisplayMetadata
                {
                    SlotIndex = ReadInt(memberElement, "slotIndex") ?? 0,
                    FullName = ReadString(memberElement, "fullName"),
                    Subtitle = ReadString(memberElement, "subtitle"),
                    Detail = ReadString(memberElement, "detail")
                });
            }
        }

        result = new TrayMetadataResult
        {
            TrayItemPath = Path.GetFullPath(trayItemPath),
            ItemType = ReadString(element, "itemType"),
            Name = ReadString(element, "name"),
            Description = ReadString(element, "description"),
            CreatorName = ReadString(element, "creatorName"),
            CreatorId = ReadString(element, "creatorId"),
            FamilySize = ReadInt(element, "familySize"),
            PendingBabies = ReadInt(element, "pendingBabies"),
            SizeX = ReadInt(element, "sizeX"),
            SizeZ = ReadInt(element, "sizeZ"),
            PriceValue = ReadInt(element, "priceValue"),
            NumBedrooms = ReadInt(element, "numBedrooms"),
            NumBathrooms = ReadInt(element, "numBathrooms"),
            Height = ReadInt(element, "height"),
            IsModdedContent = ReadBool(element, "isModdedContent"),
            Members = members
        };
        return true;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => string.Empty
        };
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue))
        {
            return numericValue;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var value) && value,
            _ => false
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private string ResolveS4TiToolPath()
    {
        lock (_gate)
        {
            if (_toolPathResolved)
            {
                return _cachedToolPath;
            }

            _toolPathResolved = true;

            foreach (var environmentVariable in ToolPathEnvironmentVariables)
            {
                var configuredValue = Environment.GetEnvironmentVariable(environmentVariable);
                foreach (var candidate in ExpandToolPathCandidate(configuredValue))
                {
                    if (IsValidToolPath(candidate))
                    {
                        _cachedToolPath = candidate;
                        return _cachedToolPath;
                    }
                }
            }

            foreach (var candidateSeed in DefaultToolPathCandidates)
            {
                foreach (var candidate in ExpandToolPathCandidate(candidateSeed))
                {
                    if (IsValidToolPath(candidate))
                    {
                        _cachedToolPath = candidate;
                        return _cachedToolPath;
                    }
                }
            }

            _cachedToolPath = string.Empty;
            return _cachedToolPath;
        }
    }

    private static IEnumerable<string> ExpandToolPathCandidate(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            yield break;
        }

        string? normalizedPath;
        try
        {
            normalizedPath = NormalizeCandidatePath(configuredPath);
        }
        catch
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            yield break;
        }

        if (!Directory.Exists(normalizedPath))
        {
            yield break;
        }

        yield return normalizedPath;

        IEnumerable<string> subDirectories;
        try
        {
            subDirectories = Directory.EnumerateDirectories(normalizedPath, "S4TI*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var subDirectory in subDirectories)
        {
            yield return subDirectory;
        }
    }

    private static string? NormalizeCandidatePath(string configuredPath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
        if (File.Exists(expanded))
        {
            return Path.GetDirectoryName(Path.GetFullPath(expanded));
        }

        if (Directory.Exists(expanded))
        {
            return Path.GetFullPath(expanded)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return null;
    }

    private static bool IsValidToolPath(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return File.Exists(Path.Combine(candidate, "S4TI.exe")) &&
               File.Exists(Path.Combine(candidate, "Sims4.UserData.dll"));
    }

    private static string ResolveWindowsPowerShellExecutable()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDirectory))
        {
            var systemPath = Path.Combine(
                windowsDirectory,
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            if (File.Exists(systemPath))
            {
                return systemPath;
            }
        }

        return string.Empty;
    }

    private static string BuildExtractionScript(
        string toolPath,
        string inputPath,
        string outputPath)
    {
        var escapedToolPath = EscapePowerShellString(toolPath);
        var escapedInputPath = EscapePowerShellString(inputPath);
        var escapedOutputPath = EscapePowerShellString(outputPath);

        return string.Join(
            Environment.NewLine,
            [
                "$ErrorActionPreference = 'Stop'",
                $"$toolDir = '{escapedToolPath}'",
                "Get-ChildItem -LiteralPath $toolDir -Filter '*.dll' | Sort-Object Name | ForEach-Object { [Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null }",
                "$trayAsm = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetType('Sims4.UserData.TrayItem', $false) } | Select-Object -First 1",
                "if ($null -eq $trayAsm) { throw 'Tray assembly not loaded.' }",
                "$trayItemType = $trayAsm.GetType('Sims4.UserData.TrayItem')",
                $"$inputPath = '{escapedInputPath}'",
                $"$outputPath = '{escapedOutputPath}'",
                "$paths = Get-Content -LiteralPath $inputPath -Raw | ConvertFrom-Json",
                "$results = New-Object System.Collections.Generic.List[object]",
                "foreach ($path in $paths) {",
                "  if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) { continue }",
                "  try {",
                "    $trayItem = $trayItemType::Open($path)",
                "    $metadata = $trayItem.Metadata",
                "    if ($null -eq $metadata) { continue }",
                "    $specific = $metadata.Metadata",
                "    $members = @()",
                "    if ($null -ne $specific -and $null -ne $specific.HhMetadata -and $null -ne $specific.HhMetadata.SimData) {",
                "      for ($i = 0; $i -lt $specific.HhMetadata.SimData.Length; $i++) {",
                "        $sim = $specific.HhMetadata.SimData[$i]",
                "        if ($null -eq $sim) { continue }",
                "        $fullName = (($sim.FirstName, $sim.LastName) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ' '",
                "        $subtitleParts = @()",
                "        if (-not [string]::IsNullOrWhiteSpace([string]$sim.Age)) { $subtitleParts += [string]$sim.Age }",
                "        if (-not [string]::IsNullOrWhiteSpace([string]$sim.Gender)) { $subtitleParts += [string]$sim.Gender }",
                "        $detailParts = @()",
                "        if (-not [string]::IsNullOrWhiteSpace([string]$sim.Species)) { $detailParts += [string]$sim.Species }",
                "        if (-not [string]::IsNullOrWhiteSpace([string]$sim.OccultTypes) -and [string]$sim.OccultTypes -ne 'Human') { $detailParts += [string]$sim.OccultTypes }",
                "        $members += [pscustomobject]@{",
                "          SlotIndex = ($i + 1)",
                "          FullName = $fullName",
                "          Subtitle = ($subtitleParts -join ' • ')",
                "          Detail = ($detailParts | Select-Object -Unique) -join ' • '",
                "        }",
                "      }",
                "    }",
                "    $results.Add([pscustomobject]@{",
                "      TrayItemPath = [string]$path",
                "      ItemType = [string]$metadata.Type",
                "      Name = [string]$metadata.Name",
                "      Description = [string]$metadata.Description",
                "      CreatorName = [string]$metadata.CreatorName",
                "      CreatorId = [string]$metadata.CreatorId",
                "      FamilySize = if ($null -ne $specific -and $null -ne $specific.HhMetadata) { [int]$specific.HhMetadata.FamilySize } else { $null }",
                "      PendingBabies = if ($null -ne $specific -and $null -ne $specific.HhMetadata) { [int]$specific.HhMetadata.PendingBabies } else { $null }",
                "      SizeX = if ($null -ne $specific -and $null -ne $specific.BpMetadata) { [int]$specific.BpMetadata.SizeX } elseif ($null -ne $specific -and $null -ne $specific.RoMetadata) { [int]$specific.RoMetadata.SizeX } else { $null }",
                "      SizeZ = if ($null -ne $specific -and $null -ne $specific.BpMetadata) { [int]$specific.BpMetadata.SizeZ } elseif ($null -ne $specific -and $null -ne $specific.RoMetadata) { [int]$specific.RoMetadata.SizeZ } else { $null }",
                "      PriceValue = if ($null -ne $specific -and $null -ne $specific.BpMetadata) { [int]$specific.BpMetadata.PriceValue } elseif ($null -ne $specific -and $null -ne $specific.RoMetadata) { [int]$specific.RoMetadata.PriceValue } else { $null }",
                "      NumBedrooms = if ($null -ne $specific -and $null -ne $specific.BpMetadata) { [int]$specific.BpMetadata.NumBedrooms } else { $null }",
                "      NumBathrooms = if ($null -ne $specific -and $null -ne $specific.BpMetadata) { [int]$specific.BpMetadata.NumBathrooms } else { $null }",
                "      Height = if ($null -ne $specific -and $null -ne $specific.RoMetadata) { [int]$specific.RoMetadata.Height } else { $null }",
                "      IsModdedContent = if ($null -ne $specific) { [bool]$specific.IsModdedContent } else { $false }",
                "      Members = @($members)",
                "    }) | Out-Null",
                "  }",
                "  catch {",
                "  }",
                "}",
                "$json = if ($results.Count -eq 0) { '[]' } else { ConvertTo-Json -InputObject @($results.ToArray()) -Depth 8 -Compress }",
                "Set-Content -LiteralPath $outputPath -Value $json -Encoding UTF8"
            ]);
    }

    private static string EscapePowerShellString(string value)
    {
        return value.Replace("'", "''");
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class CacheEntry
    {
        public long Length { get; init; }
        public DateTime LastWriteTimeUtc { get; init; }
        public required TrayMetadataResult Value { get; init; }
    }
}
