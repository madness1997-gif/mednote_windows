namespace MedNote.Core;

public enum ShutdownOutcome
{
    Completed,
    TimedOut,
    Failed,
}

public sealed record ShutdownResult
{
    public ShutdownOutcome Outcome { get; init; }

    public Exception? Error { get; init; }
}

public interface IShutdownJournal
{
    ValueTask BeginAsync(CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Stops network work synchronously, then gives local persistence and disposal
/// one shared, bounded deadline. The journal remains present after a timeout or
/// failure so the next launch can report an interrupted shutdown.
/// </summary>
public sealed class ShutdownCoordinator(IShutdownJournal journal)
{
    private readonly IShutdownJournal _journal = journal ?? throw new ArgumentNullException(nameof(journal));

    public async Task<ShutdownResult> RunAsync(
        Action stopNetwork,
        Func<CancellationToken, Task> flushLocal,
        Func<CancellationToken, Task> disposeLocal,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stopNetwork);
        ArgumentNullException.ThrowIfNull(flushLocal);
        ArgumentNullException.ThrowIfNull(disposeLocal);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        try
        {
            stopNetwork();
            await _journal.BeginAsync(cancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            var localWork = RunLocalWorkAsync(flushLocal, disposeLocal, deadline.Token);
            await localWork.WaitAsync(deadline.Token);
            await _journal.CompleteAsync(CancellationToken.None);
            return new ShutdownResult { Outcome = ShutdownOutcome.Completed };
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return new ShutdownResult { Outcome = ShutdownOutcome.TimedOut, Error = exception };
        }
        catch (TimeoutException exception)
        {
            return new ShutdownResult { Outcome = ShutdownOutcome.TimedOut, Error = exception };
        }
        catch (OperationCanceledException exception)
        {
            return new ShutdownResult { Outcome = ShutdownOutcome.TimedOut, Error = exception };
        }
        catch (Exception exception)
        {
            return new ShutdownResult { Outcome = ShutdownOutcome.Failed, Error = exception };
        }
    }

    private static async Task RunLocalWorkAsync(
        Func<CancellationToken, Task> flushLocal,
        Func<CancellationToken, Task> disposeLocal,
        CancellationToken cancellationToken)
    {
        await flushLocal(cancellationToken);
        await disposeLocal(cancellationToken);
    }
}
