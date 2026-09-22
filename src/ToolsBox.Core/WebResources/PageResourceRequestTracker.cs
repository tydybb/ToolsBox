namespace ToolsBox.Core.WebResources;

public sealed class PageResourceRequestTracker
{
    private sealed class PendingRequest(long generation)
    {
        public long Generation { get; } = generation;
        public int Count { get; set; } = 1;
        public bool IsAmbiguous { get; set; }
    }

    private readonly int _capacity;
    private readonly Dictionary<(string Method, string Url), PendingRequest> _pending = [];

    public PageResourceRequestTracker(int capacity = 8192)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public bool IsSaturated { get; private set; }

    // Both events must be observed on the browser's UI thread, including responses
    // whose status or content will later be excluded from the resource list.
    public void ObserveRequest(string method, string url, long generation)
    {
        if (IsSaturated) return;
        var key = (method, url);
        if (_pending.TryGetValue(key, out var request))
        {
            if (request.Count == int.MaxValue) { IsSaturated = true; return; }
            request.Count++;
            request.IsAmbiguous |= request.Generation != generation;
        }
        else if (_pending.Count < _capacity) _pending.Add(key, new PendingRequest(generation));
        // Unrecorded requests could later impersonate newly recorded responses for
        // the same URL. Once capacity is exceeded, only a new tracker can be trusted.
        else IsSaturated = true;
    }

    public bool ConsumeResponse(string method, string url, long generation)
    {
        if (IsSaturated) return false;
        var key = (method, url);
        if (!_pending.TryGetValue(key, out var request)) return false;
        bool current = !request.IsAmbiguous && request.Generation == generation;
        // Response ordering is unknown. A mixed-generation group stays ambiguous
        // until every pending response is consumed; failed requests are not expired.
        if (--request.Count == 0) _pending.Remove(key);
        return current;
    }
}
