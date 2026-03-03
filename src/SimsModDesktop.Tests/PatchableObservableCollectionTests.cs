using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Tests;

public sealed class PatchableObservableCollectionTests
{
    [Fact]
    public void PatchByKey_UpdatesExistingInstanceInPlace()
    {
        var first = new MutableItem("item-1", "fast");
        var second = new MutableItem("item-2", "fast");
        var collection = new PatchableObservableCollection<MutableItem>
        {
            first,
            second
        };

        collection.PatchByKey(
            new[] { new PatchItem("item-1", "deep") },
            existing => existing.Key,
            update => update.Key,
            (existing, update) => existing.Value = update.Value);

        Assert.Same(first, collection[0]);
        Assert.Equal("deep", collection[0].Value);
        Assert.Equal("fast", collection[1].Value);
    }

    private sealed record PatchItem(string Key, string Value);

    private sealed class MutableItem
    {
        public MutableItem(string key, string value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }
        public string Value { get; set; }
    }
}
