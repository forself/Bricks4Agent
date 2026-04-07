namespace BrokerCore.Services;

public sealed class WorkerIdentityAuthOptions
{
    public bool Enforce { get; set; }
    public int ClockSkewSeconds { get; set; } = 300;
    public List<WorkerCredentialRecord> Credentials { get; set; } = new();
    public List<WorkerRouteRule> HttpRoutes { get; set; } = new();
}

public static class WorkerIdentityHeaders
{
    public const string WorkerType = "X-B4A-Worker-Type";
    public const string KeyId = "X-B4A-Key-Id";
    public const string Timestamp = "X-B4A-Timestamp";
    public const string Nonce = "X-B4A-Nonce";
    public const string Signature = "X-B4A-Signature";
}

public sealed class WorkerCredentialRecord
{
    public string WorkerType { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string SharedSecret { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
}

public sealed class WorkerRouteRule
{
    public string WorkerType { get; set; } = string.Empty;
    public List<string> Paths { get; set; } = new();
}

public sealed class WorkerHttpAuthRequest
{
    public string WorkerType { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public sealed class WorkerRegisterAuthRequest
{
    public string WorkerType { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string WorkerId { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = new();
    public int MaxConcurrent { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public sealed class WorkerAuthDecision
{
    public bool IsAuthorized { get; init; }
    public int StatusCode { get; init; }
    public string Reason { get; init; } = string.Empty;

    public static WorkerAuthDecision Allow() => new() { IsAuthorized = true, StatusCode = 200 };
    public static WorkerAuthDecision Deny(int statusCode, string reason) => new()
    {
        IsAuthorized = false,
        StatusCode = statusCode,
        Reason = reason
    };
}
