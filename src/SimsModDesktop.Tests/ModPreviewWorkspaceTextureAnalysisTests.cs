using Avalonia.Threading;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.ViewModels.Preview;

namespace SimsModDesktop.Tests;

public sealed class ModPreviewWorkspaceTextureAnalysisTests
{
    [Fact]
    public async Task InspectLoadAsync_ShowsTextureCandidatesFromIndexedItem()
    {
        var inspect = new ModItemInspectViewModel(
            new FakeInspectService(),
            NullModPackageTextureEditService.Instance,
            new FakeFileDialogService());

        await inspect.LoadAsync("item-1");
        Dispatcher.UIThread.RunJobs(null);

        Assert.True(inspect.HasSelection);
        Assert.Single(inspect.TextureCandidates);
        Assert.Contains("Textures 1", inspect.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeInspectService : IModItemInspectService
    {
        public Task<ModItemInspectDetail?> TryGetAsync(string itemKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ModItemInspectDetail?>(new ModItemInspectDetail
            {
                ItemKey = itemKey,
                DisplayName = "CAS 00000001",
                EntityKind = "Cas",
                EntitySubType = "CAS Part",
                PackagePath = @"D:\Mods\demo.package",
                SourceResourceKey = "034AEECB:00000000:0000000000000001",
                SourceGroupText = "00000000",
                UpdatedUtcTicks = DateTime.UtcNow.Ticks,
                HasTextureData = true,
                PrimaryTextureResourceKey = "00B2D882:00000000:0000000000000001",
                UnclassifiedEntityCountForPackage = 0,
                TextureCount = 1,
                EditableTextureCount = 1,
                TextureRows =
                [
                    new ModPackageTextureCandidate
                    {
                        ResourceKeyText = "00B2D882:00000000:0000000000000001",
                        ContainerKind = "DDS",
                        Format = "DXT5",
                        Width = 2048,
                        Height = 2048,
                        MipMapCount = 11,
                        Editable = true,
                        SuggestedAction = "Keep",
                        Notes = "Linked texture",
                        SizeBytes = 1024
                    }
                ]
            });
        }
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public Task<IReadOnlyList<string>> PickFolderPathsAsync(string title, bool allowMultiple)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        public Task<string?> PickFilePathAsync(string title, string fileTypeName, IReadOnlyList<string> patterns)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickCsvSavePathAsync(string title, string suggestedFileName)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
