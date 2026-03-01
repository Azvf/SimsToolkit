using System.Diagnostics;
using System.Text;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class TrayEmbeddedImageExtractor
{
    private static readonly HashSet<string> DirectThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bpi",
        ".rmi",
        ".hhi",
        ".sgi"
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

    private readonly object _toolPathGate = new();

    private bool _toolPathResolved;
    private string _cachedToolPath = string.Empty;

    internal ExtractedTrayImage? TryExtractBestImage(
        SimsTrayPreviewItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var trayItemPath = ResolveTrayItemPath(item);
        if (string.IsNullOrWhiteSpace(trayItemPath))
        {
            return null;
        }

        return TryExtractWithS4Ti(trayItemPath, cancellationToken);
    }

    internal ExtractedTrayImage? TryExtractBestImage(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(filePath);
        if (DirectThumbnailExtensions.Contains(extension))
        {
            var s4tiImage = TryExtractWithS4TiFile(filePath, cancellationToken);
            if (s4tiImage is not null)
            {
                return s4tiImage;
            }
        }

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            return TrayImagePayloadScanner.TryExtractBestImage(bytes);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private ExtractedTrayImage? TryExtractWithS4Ti(
        string trayItemPath,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var toolPath = ResolveS4TiToolPath();
        if (string.IsNullOrWhiteSpace(toolPath))
        {
            return null;
        }

        var powerShellExecutable = ResolveWindowsPowerShellExecutable();
        if (string.IsNullOrWhiteSpace(powerShellExecutable))
        {
            return null;
        }

        string outputPath = string.Empty;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputDirectory = Path.Combine(Path.GetTempPath(), "SimsModDesktop", "TrayPreview");
            Directory.CreateDirectory(outputDirectory);
            outputPath = Path.Combine(outputDirectory, $"{Guid.NewGuid():N}.png");

            var encodedCommand = Convert.ToBase64String(
                Encoding.Unicode.GetBytes(BuildExtractionScript(toolPath, trayItemPath, outputPath)));

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

            if (!process.Start())
            {
                return null;
            }

            using var cancellationRegistration = cancellationToken.Register(() => TryKillProcess(process));
            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                return null;
            }

            var pngBytes = File.ReadAllBytes(outputPath);
            if (!TrayImageCodec.TryMeasure(pngBytes, out var width, out var height))
            {
                return null;
            }

            return new ExtractedTrayImage
            {
                Data = pngBytes,
                Width = width,
                Height = height
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                TryDeleteFile(outputPath);
            }
        }
    }

    private ExtractedTrayImage? TryExtractWithS4TiFile(
        string sourceFilePath,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var toolPath = ResolveS4TiToolPath();
        if (string.IsNullOrWhiteSpace(toolPath))
        {
            return null;
        }

        var powerShellExecutable = ResolveWindowsPowerShellExecutable();
        if (string.IsNullOrWhiteSpace(powerShellExecutable))
        {
            return null;
        }

        string outputPath = string.Empty;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputDirectory = Path.Combine(Path.GetTempPath(), "SimsModDesktop", "TrayPreview");
            Directory.CreateDirectory(outputDirectory);
            outputPath = Path.Combine(outputDirectory, $"{Guid.NewGuid():N}.png");

            var encodedCommand = Convert.ToBase64String(
                Encoding.Unicode.GetBytes(BuildDirectFileExtractionScript(toolPath, sourceFilePath, outputPath)));

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

            if (!process.Start())
            {
                return null;
            }

            using var cancellationRegistration = cancellationToken.Register(() => TryKillProcess(process));
            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                return null;
            }

            var pngBytes = File.ReadAllBytes(outputPath);
            if (!TrayImageCodec.TryMeasure(pngBytes, out var width, out var height))
            {
                return null;
            }

            return new ExtractedTrayImage
            {
                Data = pngBytes,
                Width = width,
                Height = height
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                TryDeleteFile(outputPath);
            }
        }
    }

    private string ResolveS4TiToolPath()
    {
        lock (_toolPathGate)
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

        if (Directory.Exists(normalizedPath))
        {
            yield return normalizedPath;

            IEnumerable<string> subDirectories;
            try
            {
                subDirectories = Directory.EnumerateDirectories(normalizedPath, "S4TI*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (IOException)
            {
                yield break;
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var subDirectory in subDirectories)
            {
                yield return subDirectory;
            }
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

    private static string ResolveTrayItemPath(SimsTrayPreviewItem item)
    {
        var trayItemPath = item.SourceFilePaths
            .FirstOrDefault(path => string.Equals(
                Path.GetExtension(path),
                ".trayitem",
                StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(trayItemPath) && File.Exists(trayItemPath))
        {
            return trayItemPath;
        }

        if (string.IsNullOrWhiteSpace(item.TrayRootPath) || string.IsNullOrWhiteSpace(item.TrayItemKey))
        {
            return string.Empty;
        }

        var candidate = Path.Combine(item.TrayRootPath, item.TrayItemKey + ".trayitem");
        return File.Exists(candidate) ? candidate : string.Empty;
    }

    private static string BuildExtractionScript(
        string toolPath,
        string trayItemPath,
        string outputPath)
    {
        var escapedToolPath = EscapePowerShellString(toolPath);
        var escapedTrayItemPath = EscapePowerShellString(trayItemPath);
        var escapedOutputPath = EscapePowerShellString(outputPath);

        return string.Join(
            Environment.NewLine,
            [
                "$ErrorActionPreference = 'Stop'",
                $"$toolDir = '{escapedToolPath}'",
                "Get-ChildItem -LiteralPath $toolDir -Filter '*.dll' | Sort-Object Name | ForEach-Object { [Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null }",
                "$exeAsm = [Reflection.Assembly]::LoadFrom((Join-Path $toolDir 'S4TI.exe'))",
                "$trayAsm = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetType('Sims4.UserData.TrayItem', $false) } | Select-Object -First 1",
                "if ($null -eq $trayAsm) { throw 'Tray assembly not loaded.' }",
                "$trayItemType = $trayAsm.GetType('Sims4.UserData.TrayItem')",
                $"$trayItem = $trayItemType::Open('{escapedTrayItemPath}')",
                "$viewType = $exeAsm.GetType('Sims4.TrayImporterApp.Controls.GeneralViewAlt')",
                "$view = [Activator]::CreateInstance($viewType)",
                "$importMethod = $viewType.GetMethod('ImportImagesNew', [Reflection.BindingFlags]'Instance,NonPublic')",
                "$items = $importMethod.Invoke($view, @($trayItem))",
                "if ($null -eq $items) { exit 4 }",
                "$selectedImage = $null",
                "foreach ($item in $items) {",
                "  if ($null -eq $item) { continue }",
                "  try {",
                "    $loaded = $item.LoadImage()",
                "  }",
                "  catch {",
                "    continue",
                "  }",
                "  if (-not $loaded) { continue }",
                "  if ($null -ne $item.Image) {",
                "    $selectedImage = $item.Image",
                "    break",
                "  }",
                "  if ($null -ne $item.SmallImage) {",
                "    $selectedImage = $item.SmallImage",
                "    break",
                "  }",
                "}",
                "if ($null -eq $selectedImage) { exit 3 }",
                $"$outputPath = '{escapedOutputPath}'",
                "$outputDirectory = Split-Path -Path $outputPath -Parent",
                "if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) { [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null }",
                "$selectedImage.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)",
                "$selectedImage.Dispose()",
                "exit 0"
            ]);
    }

    private static string BuildDirectFileExtractionScript(
        string toolPath,
        string sourceFilePath,
        string outputPath)
    {
        var escapedToolPath = EscapePowerShellString(toolPath);
        var escapedSourceFilePath = EscapePowerShellString(sourceFilePath);
        var escapedOutputPath = EscapePowerShellString(outputPath);

        return string.Join(
            Environment.NewLine,
            [
                "$ErrorActionPreference = 'Stop'",
                $"$toolDir = '{escapedToolPath}'",
                "Get-ChildItem -LiteralPath $toolDir -Filter '*.dll' | Sort-Object Name | ForEach-Object { [Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null }",
                "$exeAsm = [Reflection.Assembly]::LoadFrom((Join-Path $toolDir 'S4TI.exe'))",
                "$thumbnailType = $exeAsm.GetType('Sims4.TrayImporterApp.ItemData.ThumbnailItem')",
                $"$item = [Activator]::CreateInstance($thumbnailType, @('{escapedSourceFilePath}', 'Tray Preview'))",
                "$loaded = $item.LoadImage()",
                "if (-not $loaded) { exit 3 }",
                "$selectedImage = $item.Image",
                "if ($null -eq $selectedImage) { $selectedImage = $item.SmallImage }",
                "if ($null -eq $selectedImage) { exit 4 }",
                $"$outputPath = '{escapedOutputPath}'",
                "$outputDirectory = Split-Path -Path $outputPath -Parent",
                "if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) { [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null }",
                "$selectedImage.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)",
                "$selectedImage.Dispose()",
                "exit 0"
            ]);
    }

    private static string EscapePowerShellString(string value)
    {
        return value.Replace("'", "''");
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

        return IsExecutableOnPath("powershell.exe")
            ? "powershell.exe"
            : string.Empty;
    }

    private static bool IsExecutableOnPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        foreach (var directory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (File.Exists(Path.Combine(directory, executableName)))
            {
                return true;
            }
        }

        return false;
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
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
