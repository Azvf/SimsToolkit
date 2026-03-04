using SimsModDesktop.Application.Settings;
using SharpIniConfiguration = SharpConfig.Configuration;

namespace SimsModDesktop.Infrastructure.Settings;

public sealed class IniDebugConfigStore : IDebugConfigStore
{
    private const string DebugSectionName = "debug";
    private const string DebugSectionComment =
        "SimsModDesktop debug toggles. Use true/false. Missing keys fall back to built-in defaults.";
    private readonly string _iniPath;

    public IniDebugConfigStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimsModDesktop",
            "debug-config.ini"))
    {
    }

    public IniDebugConfigStore(string iniPath)
    {
        _iniPath = iniPath;
    }

    public Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = LoadConfigurationOrEmpty();
        if (!File.Exists(_iniPath))
        {
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!config.Contains(DebugSectionName))
        {
            return Task.FromResult<IReadOnlyDictionary<string, string>>(entries);
        }

        var section = config[DebugSectionName];
        foreach (var setting in section)
        {
            if (string.IsNullOrWhiteSpace(setting.Name))
            {
                continue;
            }

            entries[setting.Name.Trim()] = setting.StringValue?.Trim() ?? string.Empty;
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(entries);
    }

    public Task SaveAsync(IReadOnlyDictionary<string, string> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        var config = LoadConfigurationOrEmpty();
        var sectionCreated = false;
        var section = GetOrCreateDebugSection(config, ref sectionCreated);
        var changed = sectionCreated || EnsureDebugSectionComment(section);

        foreach (var entry in entries.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = entry.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = entry.Value?.Trim() ?? string.Empty;
            var existed = section.Contains(key);
            var setting = section[key];
            if (!existed)
            {
                setting.StringValue = value;
                changed = true;
                continue;
            }

            if (!string.Equals(setting.StringValue, value, StringComparison.Ordinal))
            {
                setting.StringValue = value;
                changed = true;
            }
        }

        if (!File.Exists(_iniPath))
        {
            changed = true;
        }

        if (changed)
        {
            PersistConfiguration(config);
        }

        return Task.CompletedTask;
    }

    public Task EnsureTemplateAsync(
        IReadOnlyList<DebugConfigTemplateEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        var config = LoadConfigurationOrEmpty();
        var sectionCreated = false;
        var section = GetOrCreateDebugSection(config, ref sectionCreated);
        var changed = sectionCreated || EnsureDebugSectionComment(section);

        foreach (var entry in entries.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = entry.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var existed = section.Contains(key);
            var setting = section[key];
            if (!existed)
            {
                setting.StringValue = entry.DefaultValue?.Trim() ?? string.Empty;
                changed = true;
            }

            var description = entry.Description?.Trim() ?? string.Empty;
            if (!string.Equals(setting.PreComment, description, StringComparison.Ordinal))
            {
                setting.PreComment = description;
                changed = true;
            }
        }

        if (!File.Exists(_iniPath))
        {
            changed = true;
        }

        if (changed)
        {
            PersistConfiguration(config);
        }

        return Task.CompletedTask;
    }

    private SharpIniConfiguration LoadConfigurationOrEmpty()
    {
        if (!File.Exists(_iniPath))
        {
            return new SharpIniConfiguration();
        }

        try
        {
            return SharpIniConfiguration.LoadFromFile(_iniPath);
        }
        catch
        {
            return new SharpIniConfiguration();
        }
    }

    private SharpConfig.Section GetOrCreateDebugSection(SharpIniConfiguration config, ref bool created)
    {
        if (!config.Contains(DebugSectionName))
        {
            created = true;
        }

        return config[DebugSectionName];
    }

    private static bool EnsureDebugSectionComment(SharpConfig.Section section)
    {
        if (string.Equals(section.PreComment, DebugSectionComment, StringComparison.Ordinal))
        {
            return false;
        }

        section.PreComment = DebugSectionComment;
        return true;
    }

    private void PersistConfiguration(SharpIniConfiguration config)
    {
        var directory = Path.GetDirectoryName(_iniPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_iniPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            config.SaveToFile(tempPath);

            if (!File.Exists(_iniPath))
            {
                File.Move(tempPath, _iniPath);
                return;
            }

            try
            {
                File.Replace(tempPath, _iniPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Delete(_iniPath);
                File.Move(tempPath, _iniPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
