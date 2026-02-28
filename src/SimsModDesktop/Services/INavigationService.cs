using System.ComponentModel;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public interface INavigationService : INotifyPropertyChanged
{
    AppSection SelectedSection { get; }
    string SelectedModuleKey { get; }
    IReadOnlyList<NavigationItem> SectionItems { get; }
    IReadOnlyList<NavigationItem> CurrentModules { get; }

    void SelectSection(AppSection section);
    void SelectModule(string moduleKey);
}
