using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class TS4PathDiscoveryService : ITS4PathDiscoveryService
{
    public TS4PathDiscoveryResult Discover()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var sims4UserRoot = Path.Combine(docs, "Electronic Arts", "The Sims 4");

        var modsPath = Path.Combine(sims4UserRoot, "Mods");
        var trayPath = Path.Combine(sims4UserRoot, "Tray");
        var savesPath = Path.Combine(sims4UserRoot, "saves");

        var candidates = BuildExecutableCandidates();
        var detectedExe = candidates.FirstOrDefault(File.Exists) ?? string.Empty;

        return new TS4PathDiscoveryResult
        {
            GameExecutablePath = detectedExe,
            ModsPath = Directory.Exists(modsPath) ? modsPath : string.Empty,
            TrayPath = Directory.Exists(trayPath) ? trayPath : string.Empty,
            SavesPath = Directory.Exists(savesPath) ? savesPath : string.Empty,
            GameExecutableCandidates = candidates
        };
    }

    private static IReadOnlyList<string> BuildExecutableCandidates()
    {
        var candidates = new List<string>();
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var roots = new[]
        {
            Path.Combine(pfx86, "Origin Games", "The Sims 4"),
            Path.Combine(pf, "EA Games", "The Sims 4"),
            Path.Combine(pfx86, "Steam", "steamapps", "common", "The Sims 4"),
            Path.Combine(pf, "Steam", "steamapps", "common", "The Sims 4")
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            candidates.Add(Path.Combine(root, "Game", "Bin", "TS4_x64.exe"));
            candidates.Add(Path.Combine(root, "Game", "Bin", "TS4_DX9_x64.exe"));
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
