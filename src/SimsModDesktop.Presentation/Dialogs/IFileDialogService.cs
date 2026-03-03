namespace SimsModDesktop.Infrastructure.Dialogs;

public interface IFileDialogService
{
    Task<IReadOnlyList<string>> PickFolderPathsAsync(string title, bool allowMultiple);
    Task<string?> PickFilePathAsync(string title, string fileTypeName, IReadOnlyList<string> patterns);
    Task<string?> PickCsvSavePathAsync(string title, string suggestedFileName);
}
