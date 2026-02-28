using System.ComponentModel;

namespace SimsModDesktop.Infrastructure.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    IReadOnlyList<LanguageOption> AvailableLanguages { get; }
    string CurrentLanguageCode { get; }
    string this[string key] { get; }

    void SetLanguage(string? languageCode);

    string Format(string key, params object[] args);
}
