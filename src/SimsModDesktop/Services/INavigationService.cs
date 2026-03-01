using System.ComponentModel;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public interface INavigationService : INotifyPropertyChanged
{
    AppSection SelectedSection { get; }
    IReadOnlyList<NavigationItem> SectionItems { get; }

    void SelectSection(AppSection section);
}
