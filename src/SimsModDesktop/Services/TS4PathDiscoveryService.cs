using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class TS4PathDiscoveryService : ITS4PathDiscoveryService
{
    public TS4PathDiscoveryResult Discover()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var rootCandidate = string.IsNullOrWhiteSpace(docs)
            ? string.Empty
            : Path.Combine(docs, "Electronic Arts", "The Sims 4");
        var hasValidRoot = !string.IsNullOrWhiteSpace(rootCandidate) && Directory.Exists(rootCandidate);
        var sims4UserRoot = hasValidRoot ? rootCandidate : string.Empty;

        var modsPath = hasValidRoot
            ? Path.Combine(sims4UserRoot, "Mods")
            : string.Empty;
        var trayPath = hasValidRoot
            ? Path.Combine(sims4UserRoot, "Tray")
            : string.Empty;
        var savesPath = hasValidRoot
            ? Path.Combine(sims4UserRoot, "saves")
            : string.Empty;

        var candidates = BuildExecutableCandidates();
        var detectedExe = candidates.FirstOrDefault(File.Exists) ?? string.Empty;

        return new TS4PathDiscoveryResult
        {
            Ts4RootPath = sims4UserRoot,
            GameExecutablePath = detectedExe,
            ModsPath = modsPath,
            TrayPath = trayPath,
            SavesPath = savesPath,
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
