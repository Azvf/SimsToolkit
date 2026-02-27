namespace SimsModDesktop.Infrastructure.Dialogs;

public interface IFileDialogService
{
    Task<IReadOnlyList<string>> PickFolderPathsAsync(string title, bool allowMultiple);
    Task<string?> PickCsvSavePathAsync(string title, string suggestedFileName);
}
