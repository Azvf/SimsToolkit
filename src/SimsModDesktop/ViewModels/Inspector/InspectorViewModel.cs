using System.Collections.ObjectModel;
using System.Diagnostics;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Inspector;

public sealed class InspectorViewModel : ObservableObject
{
    private readonly IReadOnlyList<IInspectorPresenter> _presenters;
    private ActionResultRow? _selectedRow;
    private SimsAction _currentAction;
    private bool _isOpen;
    private string _status = string.Empty;

    public InspectorViewModel(IEnumerable<IInspectorPresenter> presenters)
    {
        _presenters = presenters.ToArray();
        Details = new ObservableCollection<string>();

        OpenPathCommand = new RelayCommand(OpenPath, () => _selectedRow is not null);
        CopyPathCommand = new RelayCommand(CopyPath, () => _selectedRow is not null);
        ExportSelectionCommand = new RelayCommand(ExportSelection, () => _selectedRow is not null);
        RunRelatedActionCommand = new RelayCommand(RunRelatedAction, () => _selectedRow is not null);
    }

    public ObservableCollection<string> Details { get; }

    public RelayCommand OpenPathCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public RelayCommand ExportSelectionCommand { get; }
    public RelayCommand RunRelatedActionCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ActionResultRow? SelectedRow
    {
        get => _selectedRow;
        private set
        {
            if (!SetProperty(ref _selectedRow, value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public void Update(SimsAction action, ActionResultRow? selectedRow)
    {
        _currentAction = action;
        SelectedRow = selectedRow;
        Details.Clear();

        if (selectedRow is null)
        {
            Status = "Select a result to inspect details.";
            return;
        }

        var presenter = _presenters.FirstOrDefault(item => item.CanPresent(action));
        var lines = presenter?.BuildDetails(selectedRow)
                    ?? new[]
                    {
                        $"Name: {selectedRow.Name}",
                        $"Status: {selectedRow.Status}",
                        $"Path: {selectedRow.PrimaryPath}",
                        selectedRow.RawSummary
                    };

        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            Details.Add(line);
        }

        Status = "Inspector ready.";
    }

    private void OpenPath()
    {
        if (_selectedRow is null)
        {
            return;
        }

        try
        {
            var path = _selectedRow.PrimaryPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                Status = "No path for current selection.";
                return;
            }

            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                Status = "Opened file location.";
                return;
            }

            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                Status = "Opened directory.";
                return;
            }

            Status = "Path does not exist.";
        }
        catch (Exception ex)
        {
            Status = $"Open path failed: {ex.Message}";
        }
    }

    private void CopyPath()
    {
        if (_selectedRow is null)
        {
            return;
        }

        Status = string.IsNullOrWhiteSpace(_selectedRow.PrimaryPath)
            ? "No path to copy."
            : $"Copy path: {_selectedRow.PrimaryPath}";
    }

    private void ExportSelection()
    {
        Status = _selectedRow is null
            ? "No selected row."
            : $"Export requested for {_selectedRow.Name}.";
    }

    private void RunRelatedAction()
    {
        Status = _selectedRow is null
            ? "No selected row."
            : $"Related action requested for {_selectedRow.Name}.";
    }

    private void NotifyCommandStates()
    {
        OpenPathCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        ExportSelectionCommand.NotifyCanExecuteChanged();
        RunRelatedActionCommand.NotifyCanExecuteChanged();
    }
}
