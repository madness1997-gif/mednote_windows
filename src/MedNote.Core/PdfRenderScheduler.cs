namespace MedNote.Core;

public readonly record struct PdfRenderSchedulerSnapshot(
    int ActiveRenders,
    long InFlightBytes,
    long PeakInFlightBytes,
    long CompletedRenders);

/// <summary>
/// Keeps expensive page renders bounded independently of the PDF adapter.
/// Requests waiting for the slot observe cancellation, so a page that leaves
/// the realized viewport never blocks a newly visible page behind stale work.
/// </summary>
public sealed class PdfRenderScheduler : IDisposable
{
    public const long DefaultMaximumInFlightBytes = 96L * 1024 * 1024;

    private readonly SemaphoreSlim _slots;
    private readonly long _maximumInFlightBytes;
    private long _inFlightBytes;
    private long _peakInFlightBytes;
    private long _completedRenders;
    private int _activeRenders;
    private int _disposed;

    public PdfRenderScheduler(int maximumConcurrentRenders = 1, long maximumInFlightBytes = DefaultMaximumInFlightBytes)
    {
        _slots = new SemaphoreSlim(
            Math.Clamp(maximumConcurrentRenders, 1, 4),
            Math.Clamp(maximumConcurrentRenders, 1, 4));
        _maximumInFlightBytes = Math.Max(1L, maximumInFlightBytes);
    }

    public async ValueTask<T> RunAsync<T>(
        long estimatedBytes,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        estimatedBytes = Math.Max(1L, estimatedBytes);
        if (estimatedBytes > _maximumInFlightBytes)
        {
            throw new InvalidDataException(
                $"Một trang cần {estimatedBytes:N0} byte, vượt ngân sách render {_maximumInFlightBytes:N0} byte.");
        }

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            Interlocked.Increment(ref _activeRenders);
            var inFlight = Interlocked.Add(ref _inFlightBytes, estimatedBytes);
            UpdatePeak(inFlight);
            try
            {
                var result = await operation(cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _completedRenders);
                return result;
            }
            finally
            {
                Interlocked.Add(ref _inFlightBytes, -estimatedBytes);
                Interlocked.Decrement(ref _activeRenders);
            }
        }
        finally
        {
            _slots.Release();
        }
    }

    public PdfRenderSchedulerSnapshot Snapshot() => new(
        Volatile.Read(ref _activeRenders),
        Interlocked.Read(ref _inFlightBytes),
        Interlocked.Read(ref _peakInFlightBytes),
        Interlocked.Read(ref _completedRenders));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _slots.Dispose();
        }
    }

    private void UpdatePeak(long value)
    {
        var current = Interlocked.Read(ref _peakInFlightBytes);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref _peakInFlightBytes, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
