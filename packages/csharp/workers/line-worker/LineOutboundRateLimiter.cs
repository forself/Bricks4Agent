namespace LineWorker;

public sealed class LineOutboundRateLimitOptions
{
    public int PermitLimit { get; init; } = 20;
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);
    public int MaxTrackedKeys { get; init; } = 1024;
}

public interface ILineOutboundRateLimiter
{
    bool TryAcquire(string recipientId, string capabilityId, out TimeSpan retryAfter);
}

public sealed class LineOutboundRateLimiter : ILineOutboundRateLimiter
{
    private readonly LineOutboundRateLimitOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly Dictionary<RateLimitKey, Queue<DateTimeOffset>> _requests = new();

    public LineOutboundRateLimiter(LineOutboundRateLimitOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new LineOutboundRateLimitOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_options.PermitLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "PermitLimit must be greater than zero.");

        if (_options.Window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Window must be greater than zero.");

        if (_options.MaxTrackedKeys <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxTrackedKeys must be greater than zero.");
    }

    public bool TryAcquire(string recipientId, string capabilityId, out TimeSpan retryAfter)
    {
        var now = _timeProvider.GetUtcNow();
        var key = new RateLimitKey(recipientId, capabilityId);

        lock (_gate)
        {
            CompactExpired(now);

            if (!_requests.TryGetValue(key, out var timestamps))
            {
                if (_requests.Count >= _options.MaxTrackedKeys)
                {
                    retryAfter = _options.Window;
                    return false;
                }

                timestamps = new Queue<DateTimeOffset>();
                _requests.Add(key, timestamps);
            }

            Prune(timestamps, now);

            if (timestamps.Count >= _options.PermitLimit)
            {
                retryAfter = timestamps.Peek() + _options.Window - now;
                if (retryAfter < TimeSpan.Zero)
                    retryAfter = TimeSpan.Zero;
                return false;
            }

            timestamps.Enqueue(now);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    private void CompactExpired(DateTimeOffset now)
    {
        foreach (var pair in _requests.ToArray())
        {
            Prune(pair.Value, now);
            if (pair.Value.Count == 0)
                _requests.Remove(pair.Key);
        }
    }

    private void Prune(Queue<DateTimeOffset> timestamps, DateTimeOffset now)
    {
        while (timestamps.Count > 0 && now - timestamps.Peek() >= _options.Window)
            timestamps.Dequeue();
    }

    private readonly record struct RateLimitKey(string RecipientId, string CapabilityId);
}
