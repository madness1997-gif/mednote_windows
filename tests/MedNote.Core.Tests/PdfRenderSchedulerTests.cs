using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class PdfRenderSchedulerTests
{
    [TestMethod]
    public async Task RunAsync_SerializesLargePageRenders()
    {
        using var scheduler = new PdfRenderScheduler(maximumConcurrentRenders: 1);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;

        var first = scheduler.RunAsync(
            8_000_000,
            async _ =>
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task;
                return 1;
            }).AsTask();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = scheduler.RunAsync(
            8_000_000,
            _ =>
            {
                secondStarted = true;
                return ValueTask.FromResult(2);
            }).AsTask();

        await Task.Delay(40);
        Assert.IsFalse(secondStarted);
        Assert.AreEqual(1, scheduler.Snapshot().ActiveRenders);

        releaseFirst.TrySetResult();
        CollectionAssert.AreEqual(new[] { 1, 2 }, await Task.WhenAll(first, second));
        Assert.AreEqual(1L, scheduler.Snapshot().PeakInFlightBytes / 8_000_000L);
        Assert.AreEqual(2L, scheduler.Snapshot().CompletedRenders);
    }

    [TestMethod]
    public async Task RunAsync_CancelsWorkThatLeavesViewportWhileQueued()
    {
        using var scheduler = new PdfRenderScheduler(maximumConcurrentRenders: 1);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = scheduler.RunAsync(
            1024,
            async _ =>
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task;
                return true;
            }).AsTask();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancellation = new CancellationTokenSource();
        var queued = scheduler.RunAsync(
            1024,
            _ => ValueTask.FromResult(true),
            cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await queued;
        });
        releaseFirst.TrySetResult();
        Assert.IsTrue(await first);
        Assert.AreEqual(1L, scheduler.Snapshot().CompletedRenders);
    }
}
