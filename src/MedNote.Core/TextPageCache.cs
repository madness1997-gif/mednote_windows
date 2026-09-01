namespace MedNote.Core;

public sealed record TextPageCacheEntry(int PageIndex, long Bytes, long LastUsed);

public sealed record TextPageCacheSnapshot(long BudgetBytes, long TotalBytes, IReadOnlyList<TextPageCacheEntry> Entries);

public sealed class TextPageCache
{
    public const long DesktopBudgetBytes = 32L * 1024 * 1024;

    private readonly object _sync = new();
    private readonly Dictionary<int, Entry> _entries = [];
    private long _clock;
    private long _totalBytes;

    public TextPageCache(long budgetBytes = DesktopBudgetBytes)
    {
        BudgetBytes = Math.Max(1L, budgetBytes);
    }

    public long BudgetBytes { get; }

    public bool TryGet(int pageIndex, out PdfTextPage? page)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(pageIndex, out var entry))
            {
                page = null;
                return false;
            }

            entry.LastUsed = ++_clock;
            page = entry.Page;
            return true;
        }
    }

    public void Store(PdfTextPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var bytes = Math.Max(0L, page.EstimatedCacheBytes);

        lock (_sync)
        {
            RemoveCore(page.PageIndex);
            if (bytes > BudgetBytes)
            {
                return;
            }

            _entries.Add(page.PageIndex, new Entry(page, bytes, ++_clock));
            _totalBytes += bytes;
            EnforceBudget(page.PageIndex);
        }
    }

    public void Remove(int pageIndex)
    {
        lock (_sync)
        {
            RemoveCore(pageIndex);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _totalBytes = 0L;
        }
    }

    public TextPageCacheSnapshot Snapshot()
    {
        lock (_sync)
        {
            var entries = _entries
                .Select(pair => new TextPageCacheEntry(pair.Key, pair.Value.Bytes, pair.Value.LastUsed))
                .OrderBy(entry => entry.PageIndex)
                .ToArray();
            return new TextPageCacheSnapshot(BudgetBytes, _totalBytes, entries);
        }
    }

    private void EnforceBudget(int protectedPageIndex)
    {
        while (_totalBytes > BudgetBytes)
        {
            var candidate = _entries
                .Where(pair => pair.Key != protectedPageIndex)
                .OrderBy(pair => pair.Value.LastUsed)
                .FirstOrDefault();
            if (candidate.Value is null)
            {
                break;
            }

            RemoveCore(candidate.Key);
        }
    }

    private void RemoveCore(int pageIndex)
    {
        if (_entries.Remove(pageIndex, out var entry))
        {
            _totalBytes -= entry.Bytes;
        }
    }

    private sealed class Entry(PdfTextPage page, long bytes, long lastUsed)
    {
        public PdfTextPage Page { get; } = page;

        public long Bytes { get; } = bytes;

        public long LastUsed { get; set; } = lastUsed;
    }
}
