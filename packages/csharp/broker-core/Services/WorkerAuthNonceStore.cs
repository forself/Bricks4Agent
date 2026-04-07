using System.Collections.Concurrent;

namespace BrokerCore.Services;

public sealed class WorkerAuthNonceStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);

    public bool TryAccept(string workerType, string keyId, string nonce, DateTimeOffset expiresAt)
    {
        Cleanup(DateTimeOffset.UtcNow);
        var composite = $"{workerType}:{keyId}:{nonce}";
        return _seen.TryAdd(composite, expiresAt);
    }

    private void Cleanup(DateTimeOffset now)
    {
        foreach (var entry in _seen)
        {
            if (entry.Value <= now)
            {
                _seen.TryRemove(entry.Key, out _);
            }
        }
    }
}
