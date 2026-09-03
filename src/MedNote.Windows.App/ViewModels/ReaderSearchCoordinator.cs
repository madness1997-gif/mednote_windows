using System.Collections.ObjectModel;
using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

/// <summary>
/// Owns one document's progressive text-search lifecycle: provider binding,
/// cancellation, generation supersession and bounded-result streaming.
/// ReaderViewModel exposes the observable state but no longer manages the
/// concurrency primitives itself.
/// </summary>
public sealed class ReaderSearchCoordinator : ObservableObject, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ObservableCollection<PdfSearchMatch> _results = [];
    private PdfTextSearchService? _search;
    private CancellationTokenSource? _cancellation;
    private Task _searchTask = Task.CompletedTask;
    private long _generation;
    private int _pageCount;
    private string _status = "Nhập từ khóa để lập chỉ mục từng trang.";
    private bool _isSearching;
    private bool _disposed;

    public ObservableCollection<PdfSearchMatch> Results => _results;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public async Task ConfigureAsync(
        IPdfTextProvider? provider,
        int pageCount,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CancelCoreAsync(clearResults: true);
            _search = provider is null ? null : new PdfTextSearchService(provider);
            _pageCount = Math.Max(0, pageCount);
            Status = provider is null
                ? "PDF này không cung cấp lớp văn bản để tìm kiếm."
                : "Nhập từ khóa để lập chỉ mục từng trang.";
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Task searchTask;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CancelCoreAsync(clearResults: true);
            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellation = linkedCancellation;
            var generation = ++_generation;
            _searchTask = RunSearchAsync(query, generation, linkedCancellation.Token);
            searchTask = _searchTask;
        }
        finally
        {
            _gate.Release();
        }

        await searchTask;
    }

    public async Task CancelAsync(bool clearResults, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CancelCoreAsync(clearResults);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            await CancelCoreAsync(clearResults: true);
            _search = null;
            _disposed = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunSearchAsync(string query, long generation, CancellationToken cancellationToken)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            if (generation == _generation)
            {
                Status = _search is null
                    ? "PDF này không cung cấp lớp văn bản để tìm kiếm."
                    : "Nhập từ khóa để lập chỉ mục từng trang.";
                IsSearching = false;
            }

            return;
        }

        var search = _search;
        if (search is null)
        {
            if (generation == _generation)
            {
                Status = "PDF này không cung cấp lớp văn bản để tìm kiếm.";
            }

            return;
        }

        IsSearching = true;
        Status = $"Đang lập chỉ mục 0 / {_pageCount:N0} trang…";
        var progress = new Progress<PdfSearchProgress>(value =>
        {
            if (generation == _generation)
            {
                Status = $"Đã lập chỉ mục {value.ScannedPages:N0} / {value.TotalPages:N0} trang • {value.MatchCount:N0} kết quả";
            }
        });

        try
        {
            await foreach (var match in search.SearchAsync(
                query,
                new PdfSearchOptions { MaxResults = 500 },
                progress,
                cancellationToken))
            {
                if (generation != _generation)
                {
                    return;
                }

                Results.Add(match);
            }

            if (generation == _generation && Results.Count == 0)
            {
                Status = $"Đã lập chỉ mục {_pageCount:N0} trang • không có kết quả";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer query/document superseded this scan.
        }
        catch (Exception exception)
        {
            if (generation == _generation)
            {
                Status = $"Không tìm kiếm được: {exception.Message}";
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsSearching = false;
            }
        }
    }

    private async Task CancelCoreAsync(bool clearResults)
    {
        _generation++;
        var cancellation = _cancellation;
        _cancellation = null;
        cancellation?.Cancel();
        try
        {
            await _searchTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when a document or query is replaced.
        }
        finally
        {
            cancellation?.Dispose();
            _searchTask = Task.CompletedTask;
            IsSearching = false;
            if (clearResults)
            {
                Results.Clear();
            }
        }
    }
}
