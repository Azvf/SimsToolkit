using SimsModDesktop.Application.Saves;
using SimsModDesktop.PackageCore;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Tests;

public sealed class LoadSaveWithAppearanceLinksServiceTests
{
    [Fact]
    public async Task LoadAsync_Success_ReturnsSnapshot()
    {
        var snapshot = BuildSnapshot();
        var service = new LoadSaveWithAppearanceLinksService(
            new StubSaveAppearanceLinkService((_, _, _, _) => Task.FromResult(snapshot)));

        var result = await service.LoadAsync(new LoadSaveWithAppearanceLinksRequest
        {
            SavePath = "slot_00000001.save",
            GameRoot = "game",
            ModsRoot = "mods"
        });

        Assert.True(result.Success);
        Assert.Same(snapshot, result.Snapshot);
        Assert.Equal(snapshot.Issues, result.Issues);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public async Task LoadAsync_EmptySavePath_ReturnsInvalidRequestIssue()
    {
        var service = new LoadSaveWithAppearanceLinksService(
            new StubSaveAppearanceLinkService((_, _, _, _) => Task.FromResult(BuildSnapshot())));

        var result = await service.LoadAsync(new LoadSaveWithAppearanceLinksRequest
        {
            SavePath = " "
        });

        Assert.False(result.Success);
        Assert.Null(result.Snapshot);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("INVALID_REQUEST", issue.Code);
        Assert.Equal(Ts4AppearanceIssueSeverity.Error, issue.Severity);
        Assert.Equal(Ts4AppearanceIssueScope.Snapshot, issue.Scope);
    }

    [Fact]
    public async Task LoadAsync_IoException_ReturnsLoadFailedIssue()
    {
        var service = new LoadSaveWithAppearanceLinksService(
            new StubSaveAppearanceLinkService((_, _, _, _) => throw new IOException("io-error")));

        var result = await service.LoadAsync(new LoadSaveWithAppearanceLinksRequest
        {
            SavePath = "slot_00000002.save"
        });

        Assert.False(result.Success);
        Assert.Null(result.Snapshot);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("LOAD_FAILED", issue.Code);
        Assert.Equal(Ts4AppearanceIssueSeverity.Error, issue.Severity);
    }

    [Fact]
    public async Task LoadAsync_Canceled_ThrowsOperationCanceledException()
    {
        var service = new LoadSaveWithAppearanceLinksService(
            new StubSaveAppearanceLinkService((_, _, _, ct) => Task.FromCanceled<Ts4SimAppearanceSnapshot>(ct)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.LoadAsync(
                new LoadSaveWithAppearanceLinksRequest
                {
                    SavePath = "slot_00000003.save"
                },
                cts.Token));
    }

    [Fact]
    public async Task LoadAsync_ArgumentException_ReturnsUnexpectedErrorIssue()
    {
        var service = new LoadSaveWithAppearanceLinksService(
            new StubSaveAppearanceLinkService((_, _, _, _) => throw new ArgumentException("bad-arg")));

        var result = await service.LoadAsync(new LoadSaveWithAppearanceLinksRequest
        {
            SavePath = "slot_00000004.save"
        });

        Assert.False(result.Success);
        Assert.Null(result.Snapshot);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("UNEXPECTED_ERROR", issue.Code);
        Assert.Equal(Ts4AppearanceIssueSeverity.Error, issue.Severity);
    }

    private static Ts4SimAppearanceSnapshot BuildSnapshot()
    {
        return new Ts4SimAppearanceSnapshot
        {
            SavePath = "slot_00000001.save",
            LastWriteTimeLocal = DateTime.Now,
            Sims = Array.Empty<Ts4SimAppearanceSim>(),
            MorphGraphSummary = new Ts4MorphLinkGraph
            {
                SimModifierLinks = new Dictionary<ulong, Ts4MorphReference>(),
                SculptLinks = new Dictionary<ulong, Ts4MorphReference>(),
                Issues = Array.Empty<string>(),
                ReferencedResources = Array.Empty<Ts4MorphReferencedResourceHealth>()
            },
            ResourceStats = new Ts4AppearanceResourceStats
            {
                TotalReferences = 0,
                ResolvedReferences = 0,
                MissingReferences = 0,
                ParseFailures = 0
            },
            Issues = Array.Empty<Ts4AppearanceIssue>()
        };
    }

    private sealed class StubSaveAppearanceLinkService : ISaveAppearanceLinkService
    {
        private readonly Func<string, string, string, CancellationToken, Task<Ts4SimAppearanceSnapshot>> _handler;

        public StubSaveAppearanceLinkService(Func<string, string, string, CancellationToken, Task<Ts4SimAppearanceSnapshot>> handler)
        {
            _handler = handler;
        }

        public Task<Ts4SimAppearanceSnapshot> BuildSnapshotAsync(
            string savePath,
            string gameRoot,
            string modsRoot,
            CancellationToken cancellationToken = default)
        {
            return _handler(savePath, gameRoot, modsRoot, cancellationToken);
        }
    }
}
