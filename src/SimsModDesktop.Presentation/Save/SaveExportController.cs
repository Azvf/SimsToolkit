using SimsModDesktop.Application.Saves;
using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.Presentation.Save;

public sealed class SaveExportController
{
    private readonly ISaveHouseholdCoordinator _coordinator;

    public SaveExportController(ISaveHouseholdCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public Task<SaveHouseholdExportResult> ExportAsync(SaveHouseholdExportRequest request)
    {
        return Task.Run(() => _coordinator.Export(request));
    }

    public string BuildSummary(SaveHouseholdExportResult result)
    {
        var lines = new List<string>
        {
            $"Instance: {result.InstanceIdHex}",
            $"Directory: {result.ExportDirectory}",
            "Files:"
        };

        lines.AddRange(result.WrittenFiles
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>());

        if (result.Warnings.Count > 0)
        {
            lines.Add("Warnings:");
            lines.AddRange(result.Warnings);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
