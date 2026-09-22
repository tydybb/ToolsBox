using ToolsBox.Core.WebResources;

namespace ToolsBox.Core.Tests.WebResources;

public class PageResourceRequestTrackerTests
{
    private const string Url = "https://cdn.example/media.mp4?token=1";

    [Fact]
    public void CurrentGenerationResponseIsAcceptedExactlyOnce()
    {
        var tracker = new PageResourceRequestTracker();
        tracker.ObserveRequest("GET", Url, 1);

        Assert.True(tracker.ConsumeResponse("GET", Url, 1));
        Assert.False(tracker.ConsumeResponse("GET", Url, 1));
    }

    [Fact]
    public void ResponseWithoutObservedRequestIsRejected()
    {
        var tracker = new PageResourceRequestTracker();

        Assert.False(tracker.ConsumeResponse("GET", Url, 1));
    }

    [Fact]
    public void ConcurrentRequestsFromSameGenerationAreAccepted()
    {
        var tracker = new PageResourceRequestTracker();
        tracker.ObserveRequest("GET", Url, 1);
        tracker.ObserveRequest("GET", Url, 1);

        Assert.True(tracker.ConsumeResponse("GET", Url, 1));
        Assert.True(tracker.ConsumeResponse("GET", Url, 1));
        Assert.False(tracker.ConsumeResponse("GET", Url, 1));
    }

    [Fact]
    public void ResponseFromPreviousGenerationIsRejected()
    {
        var tracker = new PageResourceRequestTracker();
        tracker.ObserveRequest("GET", Url, 1);

        Assert.False(tracker.ConsumeResponse("GET", Url, 2));
    }

    [Fact]
    public void CompletedRequestAllowsSameUrlInNextGeneration()
    {
        var tracker = new PageResourceRequestTracker();
        tracker.ObserveRequest("GET", Url, 1);
        Assert.True(tracker.ConsumeResponse("GET", Url, 1));
        tracker.ObserveRequest("GET", Url, 2);

        Assert.True(tracker.ConsumeResponse("GET", Url, 2));
    }

    [Fact]
    public void CrossGenerationConcurrencyRejectsEveryResponseUntilGroupDrains()
    {
        var tracker = new PageResourceRequestTracker();
        tracker.ObserveRequest("GET", Url, 1);
        tracker.ObserveRequest("GET", Url, 2);

        // Arrival order is deliberately unknowable: neither response may claim generation 2.
        Assert.False(tracker.ConsumeResponse("GET", Url, 2));
        tracker.ObserveRequest("GET", Url, 2);
        Assert.False(tracker.ConsumeResponse("GET", Url, 2));
        Assert.False(tracker.ConsumeResponse("GET", Url, 2));
        tracker.ObserveRequest("GET", Url, 3);
        Assert.True(tracker.ConsumeResponse("GET", Url, 3));
    }

    [Fact]
    public void FailedRequestIsRetainedAcrossGenerationsWithoutGuessingExpiry()
    {
        var tracker = new PageResourceRequestTracker();
        tracker.ObserveRequest("GET", Url, 1);
        tracker.ObserveRequest("GET", Url, 2);
        Assert.False(tracker.ConsumeResponse("GET", Url, 2));
        tracker.ObserveRequest("GET", Url, 3);

        Assert.False(tracker.ConsumeResponse("GET", Url, 3));
    }

    [Fact]
    public void RequestMethodAndExactUrlAreSeparateKeys()
    {
        var tracker = new PageResourceRequestTracker();
        tracker.ObserveRequest("GET", Url, 1);
        tracker.ObserveRequest("POST", Url, 2);
        tracker.ObserveRequest("GET", Url + "&variant=2", 2);

        Assert.False(tracker.ConsumeResponse("GET", Url, 2));
        Assert.True(tracker.ConsumeResponse("POST", Url, 2));
        Assert.True(tracker.ConsumeResponse("GET", Url + "&variant=2", 2));
    }

    [Fact]
    public void ConsumingCompletedGroupReleasesCapacity()
    {
        var tracker = new PageResourceRequestTracker(capacity: 1);
        tracker.ObserveRequest("GET", Url, 1);
        Assert.True(tracker.ConsumeResponse("GET", Url, 1));
        tracker.ObserveRequest("GET", "https://cdn.example/image.png", 2);

        Assert.True(tracker.ConsumeResponse("GET", "https://cdn.example/image.png", 2));
    }

    [Fact]
    public void CapacityOverflowPermanentlyRejectsTrackedAndUntrackedResponses()
    {
        var tracker = new PageResourceRequestTracker(capacity: 1);
        tracker.ObserveRequest("GET", Url, 1);
        Assert.False(tracker.IsSaturated);
        tracker.ObserveRequest("GET", "https://cdn.example/overflow.png", 1);

        Assert.True(tracker.IsSaturated);
        Assert.False(tracker.ConsumeResponse("GET", Url, 1));
        Assert.False(tracker.ConsumeResponse("GET", "https://cdn.example/overflow.png", 1));
        tracker.ObserveRequest("GET", "https://cdn.example/overflow.png", 2);
        Assert.False(tracker.ConsumeResponse("GET", "https://cdn.example/overflow.png", 2));
        tracker.ObserveRequest("GET", "https://cdn.example/new.png", 2);
        Assert.False(tracker.ConsumeResponse("GET", "https://cdn.example/new.png", 2));
        Assert.True(tracker.IsSaturated);
    }

    [Fact]
    public void ConcurrentRequestsForExistingKeyDoNotConsumeAdditionalCapacity()
    {
        var tracker = new PageResourceRequestTracker(capacity: 1);
        tracker.ObserveRequest("GET", Url, 1);
        tracker.ObserveRequest("GET", Url, 1);

        Assert.False(tracker.IsSaturated);
        Assert.True(tracker.ConsumeResponse("GET", Url, 1));
        Assert.True(tracker.ConsumeResponse("GET", Url, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCapacityIsRejected(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageResourceRequestTracker(capacity));
}
