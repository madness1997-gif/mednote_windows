using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class ShutdownCoordinatorTests
{
    [TestMethod]
    public async Task Run_StopsNetworkAndClearsJournalAfterLocalWork()
    {
        var journal = new MemoryJournal();
        var order = new List<string>();
        var coordinator = new ShutdownCoordinator(journal);

        var result = await coordinator.RunAsync(
            () => order.Add("network"),
            _ =>
            {
                order.Add("flush");
                return Task.CompletedTask;
            },
            _ =>
            {
                order.Add("dispose");
                return Task.CompletedTask;
            },
            TimeSpan.FromSeconds(1));

        Assert.AreEqual(ShutdownOutcome.Completed, result.Outcome);
        CollectionAssert.AreEqual(new[] { "network", "flush", "dispose" }, order);
        Assert.IsFalse(journal.Pending);
    }

    [TestMethod]
    public async Task Run_ReturnsAtDeadlineAndLeavesRecoveryJournal()
    {
        var journal = new MemoryJournal();
        var coordinator = new ShutdownCoordinator(journal);

        var started = DateTime.UtcNow;
        var result = await coordinator.RunAsync(
            () => { },
            _ => Task.Delay(TimeSpan.FromSeconds(30)),
            _ => Task.CompletedTask,
            TimeSpan.FromMilliseconds(60));

        Assert.AreEqual(ShutdownOutcome.TimedOut, result.Outcome);
        Assert.IsTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
        Assert.IsTrue(journal.Pending);
    }

    [TestMethod]
    public async Task Run_LeavesRecoveryJournalWhenLocalFlushFails()
    {
        var journal = new MemoryJournal();
        var coordinator = new ShutdownCoordinator(journal);

        var result = await coordinator.RunAsync(
            () => { },
            _ => Task.FromException(new IOException("disk")),
            _ => Task.CompletedTask,
            TimeSpan.FromSeconds(1));

        Assert.AreEqual(ShutdownOutcome.Failed, result.Outcome);
        Assert.IsInstanceOfType<IOException>(result.Error);
        Assert.IsTrue(journal.Pending);
    }

    private sealed class MemoryJournal : IShutdownJournal
    {
        public bool Pending { get; private set; }

        public ValueTask BeginAsync(CancellationToken cancellationToken = default)
        {
            Pending = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            Pending = false;
            return ValueTask.CompletedTask;
        }
    }
}
