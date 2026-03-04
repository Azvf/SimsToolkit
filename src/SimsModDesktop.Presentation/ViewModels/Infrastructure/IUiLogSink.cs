namespace SimsModDesktop.Presentation.ViewModels.Infrastructure;

public interface IUiLogSink
{
    void ResetAll();

    void Append(string source, string message);

    void ClearSource(string source, bool appendClearedMarker);

    string GetSourceText(string source);
}
