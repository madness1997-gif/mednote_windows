namespace MedNote.Core;

public sealed record BitmapBudgetEntry<TKey>(TKey Key, long Bytes, long LastUsed, bool Pinned) where TKey : notnull;

public sealed record BitmapBudgetSnapshot<TKey>(long BudgetBytes, long TotalBytes, IReadOnlyList<BitmapBudgetEntry<TKey>> Entries) where TKey : notnull;

public sealed class BitmapBudget<TKey> where TKey : notnull
{
    public const long DesktopBudgetBytes = 192L * 1024 * 1024;

    private readonly Dictionary<TKey, Entry> _entries = [];
    private long _clock;

    public BitmapBudget(long budgetBytes = DesktopBudgetBytes)
    {
        BudgetBytes = Math.Max(1, budgetBytes);
    }

    public long BudgetBytes { get; }

    public void Report(TKey key, long bytes, Action evict, Func<bool>? pinned = null)
    {
        _entries[key] = new Entry(Math.Max(0, bytes), ++_clock, evict, pinned ?? (() => false));
        EnforceBudget(key);
    }

    public void Touch(TKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.LastUsed = ++_clock;
        }
    }

    public void Remove(TKey key) => _entries.Remove(key);

    public void Clear()
    {
        foreach (var entry in _entries.Values.ToArray())
        {
            SafeEvict(entry);
        }

        _entries.Clear();
    }

    public BitmapBudgetSnapshot<TKey> Snapshot()
    {
        var entries = _entries
            .Select(pair => new BitmapBudgetEntry<TKey>(pair.Key, pair.Value.Bytes, pair.Value.LastUsed, SafePinned(pair.Value)))
            .ToArray();
        return new BitmapBudgetSnapshot<TKey>(BudgetBytes, entries.Sum(entry => entry.Bytes), entries);
    }

    private void EnforceBudget(TKey protectedKey)
    {
        var totalBytes = _entries.Values.Sum(entry => entry.Bytes);
        if (totalBytes <= BudgetBytes)
        {
            return;
        }

        var candidates = _entries
            .Where(pair => !EqualityComparer<TKey>.Default.Equals(pair.Key, protectedKey) && !SafePinned(pair.Value))
            .OrderBy(pair => pair.Value.LastUsed)
            .ToArray();

        foreach (var candidate in candidates)
        {
            if (totalBytes <= BudgetBytes)
            {
                break;
            }

            _entries.Remove(candidate.Key);
            totalBytes -= candidate.Value.Bytes;
            SafeEvict(candidate.Value);
        }
    }

    private static bool SafePinned(Entry entry)
    {
        try
        {
            return entry.Pinned();
        }
        catch
        {
            return false;
        }
    }

    private static void SafeEvict(Entry entry)
    {
        try
        {
            entry.Evict();
        }
        catch
        {
            // A detached page is already effectively evicted.
        }
    }

    private sealed class Entry(long bytes, long lastUsed, Action evict, Func<bool> pinned)
    {
        public long Bytes { get; } = bytes;

        public long LastUsed { get; set; } = lastUsed;

        public Action Evict { get; } = evict;

        public Func<bool> Pinned { get; } = pinned;
    }
}
