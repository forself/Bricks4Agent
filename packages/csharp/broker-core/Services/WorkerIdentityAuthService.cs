using System.Security.Cryptography;
using System.Text;

namespace BrokerCore.Services;

public sealed class WorkerIdentityAuthService
{
    private readonly WorkerIdentityAuthOptions _options;
    private readonly WorkerAuthNonceStore _nonceStore;

    public WorkerIdentityAuthService(WorkerIdentityAuthOptions options, WorkerAuthNonceStore nonceStore)
    {
        _options = options;
        _nonceStore = nonceStore;
    }

    public string SignHttp(
        string workerType,
        string keyId,
        string sharedSecret,
        string method,
        string path,
        string body,
        DateTimeOffset timestamp,
        string nonce)
    {
        var baseString = BuildHttpBaseString(workerType, keyId, method, path, body, timestamp, nonce);
        return ComputeSignature(sharedSecret, baseString);
    }

    public string SignWorkerRegister(
        string workerType,
        string keyId,
        string sharedSecret,
        string workerId,
        IReadOnlyCollection<string> capabilities,
        int maxConcurrent,
        DateTimeOffset timestamp,
        string nonce)
    {
        var baseString = BuildWorkerRegisterBaseString(
            workerType,
            keyId,
            workerId,
            capabilities,
            maxConcurrent,
            timestamp,
            nonce);
        return ComputeSignature(sharedSecret, baseString);
    }

    public WorkerAuthDecision ValidateHttpRequest(WorkerHttpAuthRequest request)
    {
        if (!_options.Enforce)
        {
            return WorkerAuthDecision.Allow();
        }

        var credential = _options.Credentials.FirstOrDefault(item =>
            string.Equals(item.WorkerType, request.WorkerType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.KeyId, request.KeyId, StringComparison.Ordinal));

        if (credential == null || !string.Equals(credential.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerAuthDecision.Deny(401, "worker credential not found");
        }

        var skew = Math.Abs((DateTimeOffset.UtcNow - request.Timestamp).TotalSeconds);
        if (skew > _options.ClockSkewSeconds)
        {
            return WorkerAuthDecision.Deny(401, "worker timestamp expired");
        }

        var nonceExpiry = request.Timestamp.AddSeconds(_options.ClockSkewSeconds);
        if (!_nonceStore.TryAccept(request.WorkerType, request.KeyId, request.Nonce, nonceExpiry))
        {
            return WorkerAuthDecision.Deny(401, "worker nonce replay detected");
        }

        if (!IsHttpRouteAllowed(request.WorkerType, request.Path))
        {
            return WorkerAuthDecision.Deny(403, "worker route not allowed");
        }

        var expected = SignHttp(
            request.WorkerType,
            request.KeyId,
            credential.SharedSecret,
            request.Method,
            request.Path,
            request.Body,
            request.Timestamp,
            request.Nonce);

        if (!FixedTimeEquals(expected, request.Signature))
        {
            return WorkerAuthDecision.Deny(401, "worker signature invalid");
        }

        return WorkerAuthDecision.Allow();
    }

    public WorkerAuthDecision ValidateWorkerRegister(WorkerRegisterAuthRequest request)
    {
        if (!_options.Enforce)
        {
            return WorkerAuthDecision.Allow();
        }

        var credential = _options.Credentials.FirstOrDefault(item =>
            string.Equals(item.WorkerType, request.WorkerType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.KeyId, request.KeyId, StringComparison.Ordinal));

        if (credential == null || !string.Equals(credential.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerAuthDecision.Deny(401, "worker credential not found");
        }

        var skew = Math.Abs((DateTimeOffset.UtcNow - request.Timestamp).TotalSeconds);
        if (skew > _options.ClockSkewSeconds)
        {
            return WorkerAuthDecision.Deny(401, "worker timestamp expired");
        }

        var nonceExpiry = request.Timestamp.AddSeconds(_options.ClockSkewSeconds);
        if (!_nonceStore.TryAccept(request.WorkerType, request.KeyId, request.Nonce, nonceExpiry))
        {
            return WorkerAuthDecision.Deny(401, "worker nonce replay detected");
        }

        var expected = SignWorkerRegister(
            request.WorkerType,
            request.KeyId,
            credential.SharedSecret,
            request.WorkerId,
            request.Capabilities,
            request.MaxConcurrent,
            request.Timestamp,
            request.Nonce);

        if (!FixedTimeEquals(expected, request.Signature))
        {
            return WorkerAuthDecision.Deny(401, "worker signature invalid");
        }

        return WorkerAuthDecision.Allow();
    }

    private bool IsHttpRouteAllowed(string workerType, string path)
    {
        var rules = _options.HttpRoutes
            .Where(item => string.Equals(item.WorkerType, workerType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (rules.Count == 0)
        {
            return false;
        }

        return rules.SelectMany(item => item.Paths)
            .Any(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildHttpBaseString(
        string workerType,
        string keyId,
        string method,
        string path,
        string body,
        DateTimeOffset timestamp,
        string nonce)
    {
        return string.Join('\n', new[]
        {
            method.ToUpperInvariant(),
            path,
            Sha256Hex(body ?? string.Empty),
            workerType,
            keyId,
            timestamp.ToUniversalTime().ToString("O"),
            nonce
        });
    }

    private static string BuildWorkerRegisterBaseString(
        string workerType,
        string keyId,
        string workerId,
        IReadOnlyCollection<string> capabilities,
        int maxConcurrent,
        DateTimeOffset timestamp,
        string nonce)
    {
        return string.Join('\n', new[]
        {
            "WORKER_REGISTER",
            workerType,
            keyId,
            workerId,
            Sha256Hex(string.Join('\n', capabilities ?? Array.Empty<string>())),
            maxConcurrent.ToString(),
            timestamp.ToUniversalTime().ToString("O"),
            nonce
        });
    }

    private static string ComputeSignature(string sharedSecret, string baseString)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(baseString));
        return Convert.ToBase64String(bytes);
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
