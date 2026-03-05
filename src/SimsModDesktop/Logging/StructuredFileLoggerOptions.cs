namespace SimsModDesktop.Logging;

internal sealed class StructuredFileLoggerOptions
{
    public string FilePath { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimsModDesktop",
            "Cache",
            "Logs",
            "application.log.jsonl");
}
