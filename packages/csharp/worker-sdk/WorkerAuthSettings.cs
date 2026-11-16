namespace WorkerSdk;

/// <summary>Resolves worker auth values while preserving an explicit local unauthenticated opt-in.</summary>
public sealed record WorkerAuthSettings(string WorkerType, string KeyId, string SharedSecret)
{
    public static WorkerAuthSettings Resolve(
        bool allowUnauthenticated,
        string? workerType,
        string defaultWorkerType,
        string? keyId,
        string? sharedSecret,
        string defaultKeyId = "",
        string defaultSharedSecret = "",
        bool whitespaceUsesFallback = false)
    {
        var resolvedWorkerType = ResolveValue(workerType, defaultWorkerType, whitespaceUsesFallback);
        if (allowUnauthenticated)
            return new WorkerAuthSettings(resolvedWorkerType, string.Empty, string.Empty);

        return new WorkerAuthSettings(
            resolvedWorkerType,
            ResolveValue(keyId, defaultKeyId, whitespaceUsesFallback),
            ResolveValue(sharedSecret, defaultSharedSecret, whitespaceUsesFallback));
    }

    private static string ResolveValue(string? value, string fallback, bool whitespaceUsesFallback)
    {
        return value is null || (whitespaceUsesFallback && string.IsNullOrWhiteSpace(value))
            ? fallback
            : value;
    }
}
