using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using SimsModDesktop.Application.Services;

namespace SimsModDesktop.Infrastructure.Services;

/// <summary>
/// 跨平台文件操作服务实现
/// </summary>
public sealed class CrossPlatformFileOperationService : IFileOperationService
{
    private readonly ILogger<CrossPlatformFileOperationService> _logger;

    public CrossPlatformFileOperationService(ILogger<CrossPlatformFileOperationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsRecycleBinSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <inheritdoc />
    public PlatformID CurrentPlatform => Environment.OSVersion.Platform;

    /// <inheritdoc />
    public async Task<bool> MoveToRecycleBinAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("Attempted to move empty path to recycle bin");
            return false;
        }

        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                _logger.LogWarning("Path does not exist: {Path}", path);
                return false;
            }

            if (IsRecycleBinSupported)
            {
                return MoveToWindowsRecycleBin(path);
            }

            return await MoveToPlatformTrashAsync(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move {Path} to recycle bin", path);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteFileAsync(string path, bool permanent = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("Attempted to delete empty file path");
            return false;
        }

        try
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("File does not exist: {Path}", path);
                return false;
            }

            if (permanent)
            {
                File.Delete(path);
                _logger.LogInformation("Permanently deleted file: {Path}", path);
                return true;
            }

            return await MoveToRecycleBinAsync(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file: {Path}", path);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDirectoryAsync(string path, bool permanent = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("Attempted to delete empty directory path");
            return false;
        }

        try
        {
            if (!Directory.Exists(path))
            {
                _logger.LogWarning("Directory does not exist: {Path}", path);
                return false;
            }

            if (permanent)
            {
                Directory.Delete(path, recursive: true);
                _logger.LogInformation("Permanently deleted directory: {Path}", path);
                return true;
            }

            return await MoveToRecycleBinAsync(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete directory: {Path}", path);
            return false;
        }
    }

    /// <inheritdoc />
    public string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? fullPath.Replace('/', '\\')
                : fullPath.Replace('\\', '/');
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to normalize path: {Path}", path);
            return path;
        }
    }

    /// <inheritdoc />
    public string CombinePaths(params string[] paths)
    {
        if (paths == null || paths.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Path.Combine(paths);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to combine paths: {Paths}", string.Join(", ", paths));
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public RecycleBinInfo? GetRecycleBinInfo()
    {
        if (IsRecycleBinSupported)
        {
            return new RecycleBinInfo
            {
                IsAvailable = true,
                Description = "Windows Recycle Bin"
            };
        }

        var trashPath = GetPlatformTrashPath();
        return new RecycleBinInfo
        {
            IsAvailable = !string.IsNullOrWhiteSpace(trashPath),
            Path = trashPath,
            Description = "Platform Trash"
        };
    }

    /// <inheritdoc />
    public bool IsPathRooted(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);
    }

    /// <inheritdoc />
    public string GetRelativePath(string relativeTo, string path)
    {
        if (string.IsNullOrWhiteSpace(relativeTo) || string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetRelativePath(relativeTo, path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get relative path from {RelativeTo} to {Path}", relativeTo, path);
            return path;
        }
    }

    private bool MoveToWindowsRecycleBin(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else if (Directory.Exists(path))
            {
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else
            {
                return false;
            }

            _logger.LogInformation("Moved {Path} to Windows recycle bin", path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move {Path} to Windows recycle bin", path);
            return false;
        }
    }

    private async Task<bool> MoveToPlatformTrashAsync(string path)
    {
        try
        {
            var trashDir = GetPlatformTrashPath();
            if (string.IsNullOrWhiteSpace(trashDir))
            {
                return false;
            }

            Directory.CreateDirectory(trashDir);

            var name = Path.GetFileName(path);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var destPath = Path.Combine(trashDir, $"{timestamp}_{name}");

            if (File.Exists(path))
            {
                File.Move(path, destPath);
            }
            else if (Directory.Exists(path))
            {
                Directory.Move(path, destPath);
            }
            else
            {
                return false;
            }

            _logger.LogInformation("Moved {Path} to platform trash: {TrashPath}", path, destPath);
            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move {Path} to platform trash", path);
            return false;
        }
    }

    private static string GetPlatformTrashPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return string.Empty;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(home, ".Trash");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Path.Combine(home, ".local", "share", "Trash", "files");
        }

        return string.Empty;
    }
}
