using System.Collections.Concurrent;
using PDFiumCore;

namespace MedNote.Windows.App.Infrastructure;

/// <summary>
/// Owns PDFium's process lifecycle and is the only thread allowed to invoke
/// the native API. Keeping handles on one serialized worker makes session
/// shutdown deterministic even when UI render requests overlap or cancel.
/// </summary>
internal sealed class PdfiumDispatcher : IAsyncDisposable
{
    private readonly BlockingCollection<IWorkItem> _queue = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private int _disposeState;

    public PdfiumDispatcher()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "MedNote PDFium dispatcher",
        };
        _thread.Start();
    }

    public async ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        var item = new WorkItem<T>(action, cancellationToken);
        try
        {
            _queue.Add(item, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
            item.Fail(exception);
        }

        return await item.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        var ownsDisposal = Interlocked.Exchange(ref _disposeState, 1) == 0;
        if (ownsDisposal)
        {
            _queue.CompleteAdding();
        }

        await _stopped.Task.ConfigureAwait(false);
        if (ownsDisposal)
        {
            _queue.Dispose();
        }
    }

    private void Run()
    {
        var initialized = false;
        Exception? fatalError = null;
        try
        {
            fpdfview.FPDF_InitLibrary();
            initialized = true;
            _ready.TrySetResult(true);

            foreach (var item in _queue.GetConsumingEnumerable())
            {
                item.Execute();
            }
        }
        catch (Exception exception)
        {
            fatalError = exception;
            _ready.TrySetException(exception);
        }
        finally
        {
            while (_queue.TryTake(out var pending))
            {
                pending.Fail(fatalError ?? new ObjectDisposedException(nameof(PdfiumDispatcher)));
            }

            if (initialized)
            {
                try
                {
                    fpdfview.FPDF_DestroyLibrary();
                }
                catch (Exception exception)
                {
                    fatalError ??= exception;
                }
            }

            if (fatalError is null)
            {
                _stopped.TrySetResult(true);
            }
            else
            {
                _stopped.TrySetException(fatalError);
            }
        }
    }

    private interface IWorkItem
    {
        void Execute();

        void Fail(Exception exception);
    }

    private sealed class WorkItem<T> : IWorkItem
    {
        private readonly Func<T> _action;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private int _state;

        public WorkItem(Func<T> action, CancellationToken cancellationToken)
        {
            _action = action;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.UnsafeRegister(
                static state => ((WorkItem<T>)state!).CancelWhileQueued(),
                this);
        }

        public Task<T> Task => _completion.Task;

        public void Execute()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                _cancellationRegistration.Dispose();
                return;
            }

            try
            {
                _completion.TrySetResult(_action());
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
            finally
            {
                Volatile.Write(ref _state, 2);
                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        private void CancelWhileQueued()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _completion.TrySetCanceled(_cancellationToken);
            }
        }
    }
}
