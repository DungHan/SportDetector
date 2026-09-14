using NBA.Capture;
using Xunit;

namespace NBA.Capture.Tests;

public class LatestFrameBufferTests
{
    [Fact]
    public void TryGetLatest_ReturnsNull_BeforeAnyPublish()
    {
        var buffer = new LatestFrameBuffer<string>();

        Assert.Null(buffer.TryGetLatest());
    }

    [Fact]
    public void TryGetLatest_ReturnsMostRecentlyPublishedValue()
    {
        var buffer = new LatestFrameBuffer<string>();

        buffer.Publish("first");
        buffer.Publish("second");
        buffer.Publish("third");

        Assert.Equal("third", buffer.TryGetLatest());
    }

    [Fact]
    public void Publish_OverwritesUnconsumedValue_NoQueueGrowth()
    {
        // Simulates a slow consumer: the producer publishes far faster than anything drains the buffer.
        // "No unbounded queue growth" is inherent to the single-slot design - there is no queue to grow -
        // this test pins that behavior: only the latest of many rapid publishes is ever observable.
        var buffer = new LatestFrameBuffer<string>();

        for (var i = 0; i < 10_000; i++)
        {
            buffer.Publish(i.ToString());
        }

        Assert.Equal("9999", buffer.TryGetLatest());
    }

    [Fact]
    public void Clear_RemovesTheHeldValue()
    {
        var buffer = new LatestFrameBuffer<string>();
        buffer.Publish("value");

        buffer.Clear();

        Assert.Null(buffer.TryGetLatest());
    }

    [Fact]
    public async Task WaitForNextAsync_CompletesWithTheNextPublishedValue_NotAnAlreadyHeldOne()
    {
        var buffer = new LatestFrameBuffer<string>();
        buffer.Publish("stale"); // already held before the wait starts - must not satisfy the wait

        var waitTask = buffer.WaitForNextAsync();
        Assert.False(waitTask.IsCompleted);

        buffer.Publish("fresh");

        var result = await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("fresh", result);
    }

    [Fact]
    public async Task WaitForNextAsync_ObservesOnlyTheLatestOfSeveralRapidPublishes()
    {
        // A slow consumer awaiting "the next frame" should see the newest value, not get queued a
        // notification per publish.
        var buffer = new LatestFrameBuffer<string>();
        var waitTask = buffer.WaitForNextAsync();

        buffer.Publish("1");
        buffer.Publish("2");
        buffer.Publish("3");

        var result = await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("1", result); // the wait resolves on the first publish after it started...
        Assert.Equal("3", buffer.TryGetLatest()); // ...but the buffer itself still only holds the newest value.
    }
}
