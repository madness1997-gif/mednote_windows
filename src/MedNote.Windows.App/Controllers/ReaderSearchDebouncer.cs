using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Dispatching;

namespace MedNote.Windows.App.Controllers;

/// <summary>
/// Owns the small UI debounce used by the Reader search box. Search execution
/// remains on ReaderViewModel; the window only forwards the latest query.
/// </summary>
public sealed class ReaderSearchDebouncer : IDisposable
{
    private readonly ReaderViewModel _viewModel;
    private readonly DispatcherQueueTimer _timer;
    private string _pendingQuery = string.Empty;
    private bool _disposed;

    public ReaderSearchDebouncer(ReaderViewModel viewModel, DispatcherQueue dispatcherQueue)
    {
        _viewModel = viewModel;
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(275);
        _timer.IsRepeating = false;
        _timer.Tick += OnTimerTick;
    }

    public void Queue(string query)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pendingQuery = query ?? string.Empty;
        _timer.Stop();
        _timer.Start();
    }

    public void Reset()
    {
        if (_disposed)
        {
            return;
        }

        _pendingQuery = string.Empty;
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private async void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_disposed)
        {
            return;
        }

        await _viewModel.SearchAsync(_pendingQuery);
    }
}
