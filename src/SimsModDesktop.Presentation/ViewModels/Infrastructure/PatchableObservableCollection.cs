using System.Collections.ObjectModel;

namespace SimsModDesktop.Presentation.ViewModels.Infrastructure;

public sealed class PatchableObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAllStable(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var desired = items as IList<T> ?? items.ToList();
        CheckReentrancy();

        var overlap = Math.Min(Items.Count, desired.Count);
        for (var index = 0; index < overlap; index++)
        {
            this[index] = desired[index];
        }

        while (Items.Count > desired.Count)
        {
            RemoveAt(Items.Count - 1);
        }

        for (var index = overlap; index < desired.Count; index++)
        {
            Add(desired[index]);
        }
    }

    public void PatchByKey<TUpdate, TKey>(
        IEnumerable<TUpdate> updates,
        Func<T, TKey> existingKeySelector,
        Func<TUpdate, TKey> updateKeySelector,
        Action<T, TUpdate> patchAction)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(existingKeySelector);
        ArgumentNullException.ThrowIfNull(updateKeySelector);
        ArgumentNullException.ThrowIfNull(patchAction);

        var index = Items.ToDictionary(existingKeySelector);
        foreach (var update in updates)
        {
            if (index.TryGetValue(updateKeySelector(update), out var target))
            {
                patchAction(target, update);
            }
        }
    }
}
