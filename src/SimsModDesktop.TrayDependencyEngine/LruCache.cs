namespace SimsModDesktop.TrayDependencyEngine;

internal sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _list = new();

    public LruCache(int capacity)
    {
        _capacity = Math.Max(1, capacity);
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(_capacity);
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _list.Remove(node);
            _list.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            existing.Value = new Entry(key, value);
            _list.Remove(existing);
            _list.AddFirst(existing);
            return;
        }

        var node = new LinkedListNode<Entry>(new Entry(key, value));
        _list.AddFirst(node);
        _map[key] = node;

        if (_map.Count <= _capacity)
        {
            return;
        }

        var tail = _list.Last;
        if (tail is null)
        {
            return;
        }

        _list.RemoveLast();
        _map.Remove(tail.Value.Key);
    }

    private readonly record struct Entry(TKey Key, TValue Value);
}
