using SimsModDesktop.Application.Modules;
using SimsModDesktop.ViewModels.Infrastructure;
using SimsModDesktop.Application.Validation;
using System.Collections.ObjectModel;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class MergePanelViewModel : ObservableObject, IMergeModuleState
{
    private string _targetPath = string.Empty;

    public ObservableCollection<MergeSourcePathEntryViewModel> SourcePaths { get; } = new();

    public string TargetPath
    {
        get => _targetPath;
        set => SetProperty(ref _targetPath, value);
    }

    public void AddSourcePathAfter(MergeSourcePathEntryViewModel? anchorEntry, string? path = null)
    {
        var entry = new MergeSourcePathEntryViewModel
        {
            Path = path?.Trim() ?? string.Empty
        };

        if (anchorEntry is null)
        {
            SourcePaths.Add(entry);
            return;
        }

        var anchorIndex = SourcePaths.IndexOf(anchorEntry);
        if (anchorIndex < 0 || anchorIndex == SourcePaths.Count - 1)
        {
            SourcePaths.Add(entry);
            return;
        }

        SourcePaths.Insert(anchorIndex + 1, entry);
    }

    public void RemoveSourcePath(MergeSourcePathEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        var selectedIndex = SourcePaths.IndexOf(entry);
        if (selectedIndex < 0)
        {
            return;
        }

        if (SourcePaths.Count == 1)
        {
            return;
        }

        SourcePaths.RemoveAt(selectedIndex);
    }

    public IReadOnlyList<string> CollectSourcePaths()
    {
        return SourcePaths
            .Select(entry => entry.Path.Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string SerializeSourcePaths()
    {
        return string.Join(Environment.NewLine, CollectSourcePaths());
    }

    public void ApplySourcePathsText(string? rawValue)
    {
        SourcePaths.Clear();
        foreach (var path in InputParsing.ParseDelimitedList(rawValue))
        {
            AddSourcePathAfter(anchorEntry: null, path);
        }

        if (SourcePaths.Count == 0)
        {
            AddSourcePathAfter(anchorEntry: null);
        }
    }

    public void ReplaceSourcePaths(IReadOnlyList<string> sourcePaths)
    {
        SourcePaths.Clear();
        foreach (var path in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            AddSourcePathAfter(anchorEntry: null, path);
        }

        if (SourcePaths.Count == 0)
        {
            AddSourcePathAfter(anchorEntry: null);
        }
    }
}
