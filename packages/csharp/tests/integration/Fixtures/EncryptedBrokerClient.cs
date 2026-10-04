using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrokerCore.Crypto;
using BrokerCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests.Fixtures;

/// <summary>
/// Test client for the broker's encrypted envelope protocol: ECDH P-256 handshake on
/// <c>/api/v1/sessions/register</c>, HKDF-SHA256 key derivation and AES-256-GCM envelopes whose
/// associated data binds direction, session, sequence number and request path.
///
/// The wire format is implemented here on its own (like the agent and dashboard clients), so the
/// tests exercise the broker as an external caller would. Broker services are used only by
/// <see cref="OpenSession"/>, which mints a session and scoped token directly for tests that do
/// not exercise the handshake itself; the services must come from the same host that serves the
/// requests because every host generates its own keys.
/// </summary>
public sealed class EncryptedBrokerClient
{
    public const string RegisterPath = "/api/v1/sessions/register";

    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const string SessionAlgorithm = "A256GCM";
    private const string HandshakeAlgorithm = "ECDH-ES+A256GCM";

    private static readonly byte[] HandshakeInfo = Encoding.UTF8.GetBytes("broker-handshake-v1");
    private static readonly byte[] SessionInfo = Encoding.UTF8.GetBytes("broker-session-v1");

    private readonly HttpClient _http;
    private readonly IServiceProvider _services;

    public EncryptedBrokerClient(HttpClient http, IServiceProvider services)
    {
        _http = http;
        _services = services;
    }

    /// <summary>
    /// Creates an active session with a stored session key and a scoped token for it.
    /// The token's jti is the one recorded on the session, as the register endpoint does.
    /// Principal and task ids default to fresh unique values.
    /// </summary>
    public BrokerTestSession OpenSession(string roleId, string? principalId = null, string? taskId = null)
    {
        var resolvedPrincipalId = principalId ?? NewId("prn_authz");
        var resolvedTaskId = taskId ?? NewId("task_authz");

        var revocation = _services.GetRequiredService<IRevocationService>();
        var sessions = _services.GetRequiredService<ISessionService>();
        var keyStore = _services.GetRequiredService<ISessionKeyStore>();
        var tokens = _services.GetRequiredService<IScopedTokenService>();

        var jti = BrokerCore.IdGen.New("jti");
        var epoch = revocation.GetCurrentEpoch();
        var session = sessions.RegisterSession(resolvedTaskId, resolvedPrincipalId, roleId, jti, epoch, string.Empty);

        var sessionKey = RandomNumberGenerator.GetBytes(KeyLength);
        keyStore.Store(session.SessionId, sessionKey);

        var token = tokens.GenerateToken(new ScopedTokenClaims
        {
            PrincipalId = resolvedPrincipalId,
            Jti = jti,
            TaskId = resolvedTaskId,
            SessionId = session.SessionId,
            RoleId = roleId,
            Epoch = epoch
        });

        return new BrokerTestSession(
            session.SessionId,
            sessionKey,
            token,
            resolvedPrincipalId,
            resolvedTaskId,
            roleId,
            jti);
    }

    /// <summary>
    /// Sends an encrypted request over <paramref name="channel"/>. The scoped token is placed in the
    /// encrypted body only when <paramref name="scopedToken"/> is given, so a request can travel over
    /// a valid encrypted session without any token. Encrypted responses are decrypted.
    /// </summary>
    /// <param name="payload">Null, a <see cref="JsonObject"/>, JSON object text, or any object serialisable to a JSON object.</param>
    /// <param name="method">HTTP method name sent verbatim (casing is preserved).</param>
    public async Task<BrokerReply> SendEncryptedAsync(
        BrokerTestSession channel,
        string path,
        object? payload = null,
        string? scopedToken = null,
        string method = "POST",
        string? bearerToken = null)
    {
        var seq = channel.NextSequence();
        var plaintext = BuildJsonObject(payload, scopedToken).ToJsonString();
        var envelope = Seal(
            channel.SessionKey,
            plaintext,
            $"req:{channel.SessionId}{seq}{path}",
            seq,
            SessionAlgorithm,
            RandomNumberGenerator.GetBytes(NonceLength));

        var wire = new JsonObject
        {
            ["v"] = 1,
            ["session_id"] = channel.SessionId,
            ["envelope"] = envelope
        };

        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent(wire.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        using var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        return TryOpenResponse(raw, channel.SessionId, channel.SessionKey, path, out var opened)
            ? new BrokerReply(response.StatusCode, opened, decrypted: true)
            : new BrokerReply(response.StatusCode, raw, decrypted: false);
    }

    /// <summary>Sends a request without an envelope (plain JSON body or none), optionally with a Bearer token.</summary>
    /// <param name="method">HTTP method name sent verbatim (casing is preserved).</param>
    public async Task<BrokerReply> SendPlainAsync(
        string method,
        string pathAndQuery,
        string? jsonBody = null,
        string? bearerToken = null,
        string? remoteAddress = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), pathAndQuery);
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (remoteAddress is not null)
        {
            request.Headers.Add(RemoteAddressStartupFilter.HeaderName, remoteAddress);
        }

        using var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        return new BrokerReply(response.StatusCode, raw, decrypted: false);
    }

    /// <summary>
    /// Performs the real register handshake: a fresh client ECDH key, the broker public key from
    /// <c>GET /api/v1/health</c>, and a handshake envelope carrying principal, task and optional role.
    /// On success the returned session holds the derived session key and the issued scoped token.
    /// </summary>
    /// <param name="remoteAddress">Caller address applied by <see cref="RemoteAddressStartupFilter"/>; null keeps the TestServer default.</param>
    public async Task<HandshakeResult> RegisterAsync(
        string principalId,
        string taskId,
        string? roleId = null,
        string? remoteAddress = null)
    {
        var brokerPublicKey = await GetBrokerPublicKeyAsync();

        using var clientKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var brokerKey = ECDiffieHellman.Create();
        brokerKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(brokerPublicKey), out _);
        var clientPublicKey = Convert.ToBase64String(clientKey.ExportSubjectPublicKeyInfo());

        var sharedSecret = clientKey.DeriveRawSecretAgreement(brokerKey.PublicKey);
        try
        {
            var payload = new JsonObject
            {
                ["principal_id"] = principalId,
                ["task_id"] = taskId
            };
            if (roleId is not null)
            {
                payload["role_id"] = roleId;
            }

            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            var handshakeKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, KeyLength, nonce, HandshakeInfo);
            JsonObject envelope;
            try
            {
                envelope = Seal(handshakeKey, payload.ToJsonString(), clientPublicKey + RegisterPath, 0, HandshakeAlgorithm, nonce);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(handshakeKey);
            }

            var wire = new JsonObject
            {
                ["v"] = 1,
                ["client_ephemeral_pub"] = clientPublicKey,
                ["envelope"] = envelope
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, RegisterPath)
            {
                Content = new StringContent(wire.ToJsonString(), Encoding.UTF8, "application/json")
            };
            if (remoteAddress is not null)
            {
                request.Headers.Add(RemoteAddressStartupFilter.HeaderName, remoteAddress);
            }

            using var response = await _http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();

            var sessionId = BrokerJson.ReadString(raw, "session_id");
            if (string.IsNullOrEmpty(sessionId))
            {
                return new HandshakeResult(response.StatusCode, raw, session: null);
            }

            var sessionKey = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                sharedSecret,
                KeyLength,
                Encoding.UTF8.GetBytes(sessionId),
                SessionInfo);
            if (!TryOpenResponse(raw, sessionId, sessionKey, RegisterPath, out var opened))
            {
                return new HandshakeResult(response.StatusCode, raw, session: null);
            }

            var scopedToken = BrokerJson.ReadString(opened, "data", "scoped_token");
            var session = string.IsNullOrEmpty(scopedToken)
                ? null
                : new BrokerTestSession(sessionId, sessionKey, scopedToken, principalId, taskId, roleId ?? string.Empty, jti: string.Empty);
            return new HandshakeResult(response.StatusCode, opened, session);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    public async Task<string> GetBrokerPublicKeyAsync()
    {
        using var response = await _http.GetAsync("/api/v1/health");
        var raw = await response.Content.ReadAsStringAsync();
        var key = BrokerJson.ReadString(raw, "broker_public_key");
        if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException(
                $"GET /api/v1/health did not return a broker public key (HTTP {(int)response.StatusCode}).");
        }

        return key;
    }

    public static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private static JsonObject BuildJsonObject(object? payload, string? scopedToken)
    {
        var body = payload switch
        {
            null => new JsonObject(),
            JsonObject jsonObject => jsonObject,
            string jsonText => JsonNode.Parse(jsonText) as JsonObject
                ?? throw new ArgumentException("Payload text must be a JSON object.", nameof(payload)),
            _ => JsonSerializer.SerializeToNode(payload) as JsonObject
                ?? throw new ArgumentException("Payload must serialise to a JSON object.", nameof(payload))
        };

        if (scopedToken is not null)
        {
            body["scoped_token"] = scopedToken;
        }

        return body;
    }

    private static JsonObject Seal(byte[] key, string plaintext, string associatedData, int seq, string algorithm, byte[] nonce)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagLength];
        using (var aes = new AesGcm(key, TagLength))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag, Encoding.UTF8.GetBytes(associatedData));
        }

        return new JsonObject
        {
            ["v"] = 1,
            ["alg"] = algorithm,
            ["seq"] = seq,
            ["nonce"] = Convert.ToBase64String(nonce),
            ["ciphertext"] = Convert.ToBase64String(cipherBytes),
            ["tag"] = Convert.ToBase64String(tag)
        };
    }

    /// <summary>
    /// Decrypts a response envelope when the body carries one; a body without an envelope
    /// (plain JSON, errors raised before a session key is known, empty bodies) is left as is.
    /// A body that does carry an envelope but fails authentication throws, because that means
    /// the client and broker disagree on the protocol.
    /// </summary>
    private static bool TryOpenResponse(string raw, string sessionId, byte[] sessionKey, string path, out string plaintext)
    {
        plaintext = raw;
        using var document = BrokerJson.TryParse(raw);
        if (document is null
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("envelope", out var envelope)
            || envelope.ValueKind != JsonValueKind.Object
            || !envelope.TryGetProperty("ciphertext", out var cipherElement)
            || cipherElement.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(cipherElement.GetString()))
        {
            return false;
        }

        var seq = envelope.TryGetProperty("seq", out var seqElement) && seqElement.ValueKind == JsonValueKind.Number
            ? seqElement.GetInt32()
            : 0;

        var nonce = Convert.FromBase64String(envelope.GetProperty("nonce").GetString() ?? string.Empty);
        var cipherBytes = Convert.FromBase64String(cipherElement.GetString() ?? string.Empty);
        var tag = Convert.FromBase64String(envelope.GetProperty("tag").GetString() ?? string.Empty);
        var plainBytes = new byte[cipherBytes.Length];
        using (var aes = new AesGcm(sessionKey, TagLength))
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes, Encoding.UTF8.GetBytes($"resp:{sessionId}{seq}{path}"));
        }

        plaintext = Encoding.UTF8.GetString(plainBytes);
        return true;
    }
}

/// <summary>An encrypted session usable by <see cref="EncryptedBrokerClient"/>; request sequence numbers start at 1.</summary>
public sealed class BrokerTestSession
{
    private int _sequence;

    public BrokerTestSession(
        string sessionId,
        byte[] sessionKey,
        string scopedToken,
        string principalId,
        string taskId,
        string roleId,
        string jti)
    {
        SessionId = sessionId;
        SessionKey = sessionKey;
        ScopedToken = scopedToken;
        PrincipalId = principalId;
        TaskId = taskId;
        RoleId = roleId;
        Jti = jti;
    }

    public string SessionId { get; }
    public byte[] SessionKey { get; }
    public string ScopedToken { get; }
    public string PrincipalId { get; }
    public string TaskId { get; }
    public string RoleId { get; }
    public string Jti { get; }

    public int NextSequence() => Interlocked.Increment(ref _sequence);
}

/// <summary>Status code plus the response body (decrypted when the broker returned an envelope).</summary>
public sealed class BrokerReply
{
    public BrokerReply(HttpStatusCode statusCode, string body, bool decrypted)
    {
        StatusCode = statusCode;
        Body = body;
        Decrypted = decrypted;
    }

    public HttpStatusCode StatusCode { get; }
    public int Status => (int)StatusCode;
    public string Body { get; }
    public bool Decrypted { get; }

    /// <summary>The <c>message</c> field of an API response body, when present.</summary>
    public string? Message => BrokerJson.ReadString(Body, "message");

    /// <summary>The <c>success</c> field of an API response body, when present.</summary>
    public bool? SuccessFlag => BrokerJson.ReadBoolean(Body, "success");

    public override string ToString() => BrokerJson.Describe(Status, Body, Decrypted);
}

/// <summary>Outcome of a register handshake; <see cref="Session"/> is set only when a scoped token was issued.</summary>
public sealed class HandshakeResult
{
    public HandshakeResult(HttpStatusCode statusCode, string body, BrokerTestSession? session)
    {
        StatusCode = statusCode;
        Body = body;
        Session = session;
    }

    public HttpStatusCode StatusCode { get; }
    public int Status => (int)StatusCode;
    public string Body { get; }
    public BrokerTestSession? Session { get; }

    public override string ToString() => BrokerJson.Describe(Status, Body, Session is not null);
}

internal static class BrokerJson
{
    private const int DescribeLimit = 400;

    public static JsonDocument? TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ReadString(string json, params string[] path)
    {
        using var document = TryParse(json);
        if (document is null || !TryNavigate(document.RootElement, path, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    public static bool? ReadBoolean(string json, params string[] path)
    {
        using var document = TryParse(json);
        if (document is null || !TryNavigate(document.RootElement, path, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    public static string Describe(int status, string body, bool decrypted)
    {
        var shown = body.Length > DescribeLimit ? body[..DescribeLimit] + "..." : body;
        return $"HTTP {status}{(decrypted ? " (decrypted)" : string.Empty)}: {shown}";
    }

    private static bool TryNavigate(JsonElement root, IReadOnlyList<string> path, out JsonElement element)
    {
        element = root;
        foreach (var segment in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out var next))
            {
                return false;
            }

            element = next;
        }

        return true;
    }
}
