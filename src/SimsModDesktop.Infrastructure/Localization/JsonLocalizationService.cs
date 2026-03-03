using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace SimsModDesktop.Infrastructure.Localization;

public sealed class JsonLocalizationService : ILocalizationService, IDisposable
{
    private const string DefaultLanguageCode = "en-US";
    private const string TodoSuffix = ".todo";

    private readonly object _stateLock = new();
    private readonly object _reloadLock = new();
    private readonly string _localizationDirectory;
    private readonly FileSystemWatcher? _watcher;
    private CancellationTokenSource? _reloadCts;
    private Dictionary<string, IReadOnlyDictionary<string, string>> _languages =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, bool> _todoLanguageFlags =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LanguageOption> _availableLanguages = Array.Empty<LanguageOption>();
    private IReadOnlyDictionary<string, string> _active = LocalizationDefaults.English;
    private string _currentLanguageCode = DefaultLanguageCode;

    public JsonLocalizationService()
        : this(null)
    {
    }

    public JsonLocalizationService(string? localizationDirectoryOverride)
    {
        _localizationDirectory = ResolveLocalizationDirectory(localizationDirectoryOverride);
        ApplyLoadedLanguages(LoadLanguageData(_localizationDirectory), DefaultLanguageCode);

        if (!string.IsNullOrWhiteSpace(_localizationDirectory) && Directory.Exists(_localizationDirectory))
        {
            _watcher = new FileSystemWatcher(_localizationDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnLocalizationFilesChanged;
            _watcher.Changed += OnLocalizationFilesChanged;
            _watcher.Deleted += OnLocalizationFilesChanged;
            _watcher.Renamed += OnLocalizationFilesChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LanguageOption> AvailableLanguages
    {
        get
        {
            lock (_stateLock)
            {
                return _availableLanguages;
            }
        }
    }

    public string CurrentLanguageCode
    {
        get
        {
            lock (_stateLock)
            {
                return _currentLanguageCode;
            }
        }
    }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            lock (_stateLock)
            {
                if (_active.TryGetValue(key, out var value))
                {
                    return value;
                }

                if (_languages.TryGetValue(DefaultLanguageCode, out var fallback) &&
                    fallback.TryGetValue(key, out var fallbackValue))
                {
                    return fallbackValue;
                }
            }

            if (LocalizationDefaults.English.TryGetValue(key, out var builtInFallback))
            {
                return builtInFallback;
            }

            return key;
        }
    }

    public void SetLanguage(string? languageCode)
    {
        var hasChanged = false;

        lock (_stateLock)
        {
            var normalized = NormalizeLanguageCode(languageCode, _languages);
            if (string.Equals(_currentLanguageCode, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentLanguageCode = normalized;
            _active = _languages[_currentLanguageCode];
            hasChanged = true;
        }

        if (!hasChanged)
        {
            return;
        }

        RaisePropertyChanged(nameof(CurrentLanguageCode));
        RaisePropertyChanged("Item[]");
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, this[key], args);
    }

    public void Dispose()
    {
        lock (_reloadLock)
        {
            _reloadCts?.Cancel();
            _reloadCts?.Dispose();
            _reloadCts = null;
        }

        if (_watcher is null)
        {
            return;
        }

        _watcher.Created -= OnLocalizationFilesChanged;
        _watcher.Changed -= OnLocalizationFilesChanged;
        _watcher.Deleted -= OnLocalizationFilesChanged;
        _watcher.Renamed -= OnLocalizationFilesChanged;
        _watcher.Dispose();
    }

    private void OnLocalizationFilesChanged(object sender, FileSystemEventArgs e)
    {
        QueueReload();
    }

    private void QueueReload()
    {
        CancellationToken token;
        lock (_reloadLock)
        {
            _reloadCts?.Cancel();
            _reloadCts?.Dispose();
            _reloadCts = new CancellationTokenSource();
            token = _reloadCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            ReloadLanguagesFromDirectory();
        }, token);
    }

    private void ReloadLanguagesFromDirectory()
    {
        var loaded = LoadLanguageData(_localizationDirectory);

        bool optionsChanged;
        bool languageChanged;
        lock (_stateLock)
        {
            var previousOptions = _availableLanguages;
            var previousLanguage = _currentLanguageCode;

            ApplyLoadedLanguages(loaded, _currentLanguageCode);

            optionsChanged = !AreLanguageOptionsEqual(previousOptions, _availableLanguages);
            languageChanged = !string.Equals(previousLanguage, _currentLanguageCode, StringComparison.OrdinalIgnoreCase);
        }

        if (optionsChanged)
        {
            RaisePropertyChanged(nameof(AvailableLanguages));
        }

        if (languageChanged)
        {
            RaisePropertyChanged(nameof(CurrentLanguageCode));
        }

        RaisePropertyChanged("Item[]");
    }

    private void ApplyLoadedLanguages(
        (Dictionary<string, IReadOnlyDictionary<string, string>> Languages, Dictionary<string, bool> TodoLanguageFlags) loaded,
        string? preferredLanguageCode)
    {
        var languages = loaded.Languages;
        var todoFlags = loaded.TodoLanguageFlags;
        EnsureDefaultLanguage(languages, todoFlags);

        var normalized = NormalizeLanguageCode(preferredLanguageCode, languages);

        _languages = languages;
        _todoLanguageFlags = todoFlags;
        _availableLanguages = BuildLanguageOptions(_languages, _todoLanguageFlags);
        _currentLanguageCode = normalized;
        _active = _languages[_currentLanguageCode];
    }

    private static (Dictionary<string, IReadOnlyDictionary<string, string>> Languages, Dictionary<string, bool> TodoLanguageFlags)
        LoadLanguageData(string localizationDirectory)
    {
        var languages = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var todoFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(localizationDirectory) || !Directory.Exists(localizationDirectory))
        {
            return (languages, todoFlags);
        }

        foreach (var filePath in Directory.EnumerateFiles(localizationDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (!TryParseLanguageCode(fileName, out var languageCode, out var isTodoFile))
            {
                continue;
            }

            var dictionary = TryLoadDictionary(filePath);
            if (dictionary.Count == 0)
            {
                continue;
            }

            if (languages.TryGetValue(languageCode, out _))
            {
                var existingIsTodo = todoFlags.TryGetValue(languageCode, out var todoFlag) && todoFlag;
                if (!existingIsTodo && isTodoFile)
                {
                    continue;
                }
            }

            languages[languageCode] = dictionary;
            todoFlags[languageCode] = isTodoFile;
        }

        return (languages, todoFlags);
    }

    private static void EnsureDefaultLanguage(
        IDictionary<string, IReadOnlyDictionary<string, string>> languages,
        IDictionary<string, bool> todoFlags)
    {
        if (languages.ContainsKey(DefaultLanguageCode))
        {
            return;
        }

        languages[DefaultLanguageCode] = LocalizationDefaults.English;
        todoFlags[DefaultLanguageCode] = false;
    }

    private static IReadOnlyList<LanguageOption> BuildLanguageOptions(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> languages,
        IReadOnlyDictionary<string, bool> todoFlags)
    {
        var orderedCodes = languages.Keys
            .OrderBy(code => string.Equals(code, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase);

        var result = new List<LanguageOption>();
        foreach (var code in orderedCodes)
        {
            var isTodo = todoFlags.TryGetValue(code, out var todoFlag) && todoFlag;
            result.Add(new LanguageOption(code, BuildDisplayName(code, isTodo)));
        }

        return result;
    }

    private static bool AreLanguageOptionsEqual(IReadOnlyList<LanguageOption> left, IReadOnlyList<LanguageOption> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Code, right[i].Code, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(left[i].DisplayName, right[i].DisplayName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeLanguageCode(
        string? languageCode,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> languages)
    {
        var candidate = languageCode?.Trim();
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var requested = FindCanonicalLanguageCode(candidate, languages);
            if (!string.IsNullOrWhiteSpace(requested))
            {
                return requested;
            }
        }

        return FindCanonicalLanguageCode(DefaultLanguageCode, languages) ?? languages.Keys.First();
    }

    private static string? FindCanonicalLanguageCode(
        string languageCode,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> languages)
    {
        foreach (var code in languages.Keys)
        {
            if (string.Equals(code, languageCode, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        return null;
    }

    private static bool TryParseLanguageCode(
        string? fileNameWithoutExtension,
        out string languageCode,
        out bool isTodoFile)
    {
        languageCode = string.Empty;
        isTodoFile = false;

        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return false;
        }

        var normalized = fileNameWithoutExtension.Trim();
        if (normalized.EndsWith(TodoSuffix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^TodoSuffix.Length];
            isTodoFile = true;
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        languageCode = normalized;
        return true;
    }

    private static IReadOnlyDictionary<string, string> TryLoadDictionary(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = property.Value.GetString();
                if (value is null)
                {
                    continue;
                }

                result[property.Name] = value;
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string BuildDisplayName(string languageCode, bool isTodoFile)
    {
        string displayName;
        try
        {
            var culture = CultureInfo.GetCultureInfo(languageCode);
            displayName = $"{culture.EnglishName} ({languageCode})";
        }
        catch (CultureNotFoundException)
        {
            displayName = languageCode;
        }

        if (isTodoFile)
        {
            displayName += " (TODO)";
        }

        return displayName;
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string ResolveLocalizationDirectory(string? localizationDirectoryOverride)
    {
        if (!string.IsNullOrWhiteSpace(localizationDirectoryOverride) &&
            Directory.Exists(localizationDirectoryOverride))
        {
            return localizationDirectoryOverride;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "localization"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "localization"),
            Path.Combine(Directory.GetCurrentDirectory(), "SimsModDesktop", "assets", "localization"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "SimsModDesktop", "assets", "localization")
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
