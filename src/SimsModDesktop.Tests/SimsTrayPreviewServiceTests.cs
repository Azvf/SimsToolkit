using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class SimsTrayPreviewServiceTests
{
    [Fact]
    public async Task BuildPageAsync_BuildSizeFilter_FiltersLotsAndRoomsByParsedDimensions()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "villa_small_20x20", ".trayitem", ".blueprint");
        CreateTrayFiles(trayDir.Path, "villa_large_50x40", ".trayitem", ".blueprint");

        var service = new SimsTrayPreviewService();
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PresetTypeFilter = "Lot",
            BuildSizeFilter = "20 x 20",
            HouseholdSizeFilter = "All",
            PageSize = 50
        };

        var summary = await service.BuildSummaryAsync(request);
        var page = await service.BuildPageAsync(request, pageIndex: 1);

        Assert.Equal(1, summary.TotalItems);
        var item = Assert.Single(page.Items);
        Assert.Contains("20x20", item.ItemName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPageAsync_HouseholdSizeFilter_FiltersByParsedMemberCount()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "family_3sims", ".trayitem", ".householdbinary");
        CreateTrayFiles(trayDir.Path, "family_6sims", ".trayitem", ".householdbinary");

        var service = new SimsTrayPreviewService();
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PresetTypeFilter = "Household",
            BuildSizeFilter = "All",
            HouseholdSizeFilter = "3",
            PageSize = 50
        };

        var summary = await service.BuildSummaryAsync(request);
        var page = await service.BuildPageAsync(request, pageIndex: 1);

        Assert.Equal(1, summary.TotalItems);
        var item = Assert.Single(page.Items);
        Assert.Contains("3sims", item.ItemName, StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateTrayFiles(string rootPath, string baseName, params string[] extensions)
    {
        foreach (var extension in extensions)
        {
            var filePath = Path.Combine(rootPath, $"{baseName}{extension}");
            File.WriteAllText(filePath, "fixture");
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sims-tray-svc-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
