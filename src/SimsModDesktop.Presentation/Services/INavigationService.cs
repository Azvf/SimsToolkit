using System.ComponentModel;

namespace SimsModDesktop.Presentation.Services;

public interface INavigationService : INotifyPropertyChanged
{
    AppSection SelectedSection { get; }
    IReadOnlyList<NavigationItem> SectionItems { get; }

    void SelectSection(AppSection section);
}
