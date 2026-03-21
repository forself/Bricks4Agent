using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BrokerCore.Crypto;

/// <summary>
/// E2E Bridge — 端對端測試控制台
///
/// 流程：
/// 1. ECDH 交握 → 建立 session → 取得 scoped_token
/// 2. 輪詢 LINE 訊息（via broker execution: line.message.read）
/// 3. 解析命令 → 執行（via broker execution: file.*, line.*）
/// 4. 透過 LINE 回報結果（via broker execution: line.message.send）
///
/// 用法：
///   dotnet run -- --broker http://localhost:5000
/// </summary>

var brokerUrl = "http://localhost:5000";
var principalId = "prn_podman_dev";
var taskId = "task_podman_dev";
var roleId = "role_reader";

// Parse args
for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--broker": brokerUrl = args[++i]; break;
        case "--principal": principalId = args[++i]; break;
        case "--task": taskId = args[++i]; break;
        case "--role": roleId = args[++i]; break;
    }
}

Console.WriteLine("=== E2E Bridge ===");
Console.WriteLine($"  Broker: {brokerUrl}");
Console.WriteLine($"  Principal: {principalId}");
Console.WriteLine($"  Task: {taskId}");
Console.WriteLine($"  Role: {roleId}");
Console.WriteLine();

var client = new BrokerApiClient(brokerUrl);
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// ── Step 1: 取得 Broker 公鑰 ──
Console.Write("[1/3] Health check... ");
var health = await client.GetAsync("/api/v1/health", cts.Token);
Console.WriteLine($"OK ({health.RootElement.GetProperty("status").GetString()})");

// ── Step 2: ECDH 交握 → 建立 session ──
Console.Write("[2/3] Registering session (ECDH handshake)... ");
await client.RegisterSessionAsync(principalId, taskId, roleId, brokerUrl, cts.Token);
Console.WriteLine($"OK (session={client.SessionId?[..16]}...)");
Console.WriteLine($"  Token: {client.ScopedToken?[..32]}...");

// ── Step 3: 測試各能力 ──
Console.WriteLine("[3/3] Testing capabilities...");
Console.WriteLine();

// Test: line.message.read
Console.WriteLine("── Test: line.message.read ──");
var readResult = await client.ExecuteAsync("line.message.read", "read_line_messages",
    JsonSerializer.Serialize(new { route = "read_line_messages", args = new { consume = false } }),
    cts.Token);
Console.WriteLine($"  Result: {Truncate(readResult, 200)}");
Console.WriteLine();

// Test: line.notification.send
Console.WriteLine("── Test: line.notification.send ──");
var notifyResult = await client.ExecuteAsync("line.notification.send", "send_line_notification",
    JsonSerializer.Serialize(new
    {
        route = "send_line_notification",
        args = new
        {
            level = "info",
            title = "E2E Bridge Connected",
            body = $"Session established at {DateTime.Now:HH:mm:ss}. Send me commands!"
        }
    }),
    cts.Token);
Console.WriteLine($"  Result: {Truncate(notifyResult, 200)}");
Console.WriteLine();

// ── Interactive Loop: 接收 LINE 訊息 → 執行 → 回覆 ──
Console.WriteLine("=== Interactive Mode ===");
Console.WriteLine("Polling LINE messages... (Ctrl+C to stop)");
Console.WriteLine("Commands: /list [path] | /read [path] | /search [pattern] | /exec [capability] [json]");
Console.WriteLine();

int pollCount = 0;
while (!cts.Token.IsCancellationRequested)
{
    try
    {
        // 讀取未消費的訊息
        var messagesJson = await client.ExecuteAsync("line.message.read", "read_line_messages",
            JsonSerializer.Serialize(new { route = "read_line_messages", args = new { consume = true } }),
            cts.Token);

        var messages = TryParseMessages(messagesJson);

        foreach (var msg in messages)
        {
            var text = msg.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) continue;

            Console.WriteLine($"  ← LINE: {text}");

            string response;
            try
            {
                response = await ProcessCommand(client, text, cts.Token);
            }
            catch (Exception ex)
            {
                response = $"❌ Error: {ex.Message}";
            }

            Console.WriteLine($"  → Reply: {Truncate(response, 150)}");

            // 發送回覆
            await client.ExecuteAsync("line.message.send", "send_line_message",
                JsonSerializer.Serialize(new
                {
                    route = "send_line_message",
                    args = new { text = response }
                }),
                cts.Token);
        }

        if (++pollCount % 30 == 0)
            Console.WriteLine($"  ... polling ({pollCount} cycles, {DateTime.Now:HH:mm:ss})");
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Poll error: {ex.Message}");
    }

    await Task.Delay(2000, cts.Token).ContinueWith(_ => { });
}

Console.WriteLine("\nShutting down...");

// ═══════════════════════════════════════════════
// Command Processor
// ═══════════════════════════════════════════════

async Task<string> ProcessCommand(BrokerApiClient c, string input, CancellationToken ct)
{
    // /list [path]
    if (input.StartsWith("/list", StringComparison.OrdinalIgnoreCase))
    {
        var path = input.Length > 5 ? input[5..].Trim() : "/workspace";
        var result = await c.ExecuteAsync("file.list", "list_directory",
            JsonSerializer.Serialize(new { route = "list_directory", args = new { path, depth = 2 } }), ct);
        return FormatResult("list_directory", result);
    }

    // /read [path]
    if (input.StartsWith("/read", StringComparison.OrdinalIgnoreCase))
    {
        var path = input.Length > 5 ? input[5..].Trim() : "";
        if (string.IsNullOrEmpty(path)) return "Usage: /read <file-path>";
        var result = await c.ExecuteAsync("file.read", "read_file",
            JsonSerializer.Serialize(new { route = "read_file", args = new { path, limit = 50 } }), ct);
        return FormatResult("read_file", result);
    }

    // /search [pattern]
    if (input.StartsWith("/search", StringComparison.OrdinalIgnoreCase))
    {
        var pattern = input.Length > 7 ? input[7..].Trim() : "";
        if (string.IsNullOrEmpty(pattern)) return "Usage: /search <pattern>";
        var result = await c.ExecuteAsync("file.search", "search_content",
            JsonSerializer.Serialize(new { route = "search_content", args = new { pattern, path = "/workspace", max_results = 10 } }), ct);
        return FormatResult("search_content", result);
    }

    // /approve — test approval flow
    if (input.StartsWith("/approve", StringComparison.OrdinalIgnoreCase))
    {
        var desc = input.Length > 8 ? input[8..].Trim() : "Test approval request";
        var result = await c.ExecuteAsync("line.approval.request", "request_line_approval",
            JsonSerializer.Serialize(new
            {
                route = "request_line_approval",
                args = new { description = desc, timeout_seconds = 60 }
            }), ct);
        return FormatResult("approval", result);
    }

    // /notify [message]
    if (input.StartsWith("/notify", StringComparison.OrdinalIgnoreCase))
    {
        var body = input.Length > 7 ? input[7..].Trim() : "Test notification";
        var result = await c.ExecuteAsync("line.notification.send", "send_line_notification",
            JsonSerializer.Serialize(new
            {
                route = "send_line_notification",
                args = new { level = "info", title = "Notification", body }
            }), ct);
        return FormatResult("notification", result);
    }

    // /workers — list registered workers
    if (input.StartsWith("/workers", StringComparison.OrdinalIgnoreCase))
    {
        var result = await c.GetAsync("/api/v1/workers", ct);
        return result.RootElement.GetRawText();
    }

    // Default: echo + basic math
    if (IsSimpleMath(input, out var mathResult))
        return $"🔢 {input} = {mathResult}";

    return $"Echo: {input}\n\nCommands:\n/list [path]\n/read <file>\n/search <pattern>\n/notify <msg>\n/approve <desc>\n/workers";
}

string FormatResult(string tool, string raw)
{
    try
    {
        var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        // Broker 回應格式：{ "ok": true, "data": { "result_payload": "..." } }
        string? payload = null;
        if (root.TryGetProperty("data", out var data) && data.TryGetProperty("result_payload", out var rp))
            payload = rp.ValueKind == JsonValueKind.String ? rp.GetString() : rp.GetRawText();
        else if (root.TryGetProperty("result_payload", out var rp2))
            payload = rp2.ValueKind == JsonValueKind.String ? rp2.GetString() : rp2.GetRawText();

        if (!string.IsNullOrEmpty(payload))
            return payload.Length > 4500 ? payload[..4500] + "\n...(truncated)" : payload;

        return raw.Length > 4500 ? raw[..4500] + "\n...(truncated)" : raw;
    }
    catch
    {
        return raw.Length > 4500 ? raw[..4500] + "\n...(truncated)" : raw;
    }
}

bool IsSimpleMath(string input, out string result)
{
    result = "";
    input = input.Trim();
    // Basic patterns: "1+2", "3*4", "10/2", "5-3", "1+2="
    input = input.TrimEnd('=', '?', ' ');
    try
    {
        // Very basic: split by operator
        foreach (var op in new[] { '+', '-', '*', '/' })
        {
            var parts = input.Split(op, 2);
            if (parts.Length == 2 &&
                double.TryParse(parts[0].Trim(), out var a) &&
                double.TryParse(parts[1].Trim(), out var b))
            {
                var r = op switch
                {
                    '+' => a + b,
                    '-' => a - b,
                    '*' => a * b,
                    '/' => b != 0 ? a / b : double.NaN,
                    _ => double.NaN
                };
                result = r.ToString("G");
                return true;
            }
        }
    }
    catch { }
    return false;
}

List<LineMessage> TryParseMessages(string json)
{
    try
    {
        // Broker 回應格式（解密後）：
        // { "ok": true, "data": { "request_id": "...", "execution_state": "Succeeded", "result_payload": "{...}" } }
        // result_payload 是 JSON 字串，內容為 { "count": N, "messages": [...] }
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 逐層查找 result_payload：root → data → result_payload
        string? payloadStr = null;

        // Case 1: { data: { result_payload: "..." } }（Broker 標準回應）
        if (root.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("result_payload", out var rp))
                payloadStr = rp.ValueKind == JsonValueKind.String ? rp.GetString() : rp.GetRawText();
        }
        // Case 2: { result_payload: "..." }（直接在 root）
        else if (root.TryGetProperty("result_payload", out var rp2))
        {
            payloadStr = rp2.ValueKind == JsonValueKind.String ? rp2.GetString() : rp2.GetRawText();
        }
        // Case 3: 整個 json 就是 payload（直接含 messages）
        else
        {
            payloadStr = json;
        }

        if (string.IsNullOrEmpty(payloadStr))
            return new List<LineMessage>();

        var payload = JsonDocument.Parse(payloadStr);
        if (payload.RootElement.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
        {
            var result = new List<LineMessage>();
            foreach (var m in msgs.EnumerateArray())
            {
                result.Add(new LineMessage
                {
                    Text = m.TryGetProperty("text", out var t) ? t.GetString() : null,
                    Type = m.TryGetProperty("type", out var tp) ? tp.ToString() : null,
                    UserId = m.TryGetProperty("user_id", out var u) ? u.GetString() : null,
                    Timestamp = m.TryGetProperty("timestamp", out var ts) ? ts.GetString() : null
                });
            }
            return result;
        }
    }
    catch { }
    return new List<LineMessage>();
}

string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

// ═══════════════════════════════════════════════
// Models
// ═══════════════════════════════════════════════

class LineMessage
{
    public string? Text { get; set; }
    public string? Type { get; set; }
    public string? UserId { get; set; }
    public string? Timestamp { get; set; }
}

// ═══════════════════════════════════════════════
// Broker API Client (with ECDH encryption)
// ═══════════════════════════════════════════════

class BrokerApiClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private ECDiffieHellman? _clientEcdh;
    private byte[]? _sessionKey;
    private int _seq;

    public string? SessionId { get; private set; }
    public string? ScopedToken { get; private set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public BrokerApiClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        var resp = await _http.GetAsync($"{_baseUrl}{path}", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(body);
    }

    /// <summary>ECDH handshake → register session</summary>
    public async Task RegisterSessionAsync(string principalId, string taskId, string roleId,
        string brokerUrl, CancellationToken ct)
    {
        // 1. Generate client ephemeral ECDH key pair
        _clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var clientPubBase64 = Convert.ToBase64String(
            _clientEcdh.PublicKey.ExportSubjectPublicKeyInfo());

        // 2. Build plaintext
        var plaintext = JsonSerializer.Serialize(new
        {
            principal_id = principalId,
            task_id = taskId,
            role_id = roleId
        });

        // 3. Get broker public key (from health or known)
        // For handshake, we need to encrypt with a derived handshake key
        var nonce = RandomNumberGenerator.GetBytes(12);
        var nonceBase64 = Convert.ToBase64String(nonce);

        // Get broker public key
        var healthResp = await _http.GetAsync($"{_baseUrl}/api/v1/health", ct);
        // Broker pub key needs to be obtained - let's get it from the register endpoint error or config
        // Actually, we need the broker's ECDH public key to derive the handshake key
        // The broker-client.js gets it from config. We'll need to get it similarly.

        // For now, let's get the broker pub key from the Broker
        var brokerPubKeyBase64 = await GetBrokerPubKeyAsync(ct);

        // 4. Derive handshake key: ECDH shared secret + HKDF(salt=nonce, info="broker-handshake-v1")
        var brokerPubBytes = Convert.FromBase64String(brokerPubKeyBase64);
        using var brokerEcdh = ECDiffieHellman.Create();
        brokerEcdh.ImportSubjectPublicKeyInfo(brokerPubBytes, out _);

        var sharedSecret = _clientEcdh.DeriveRawSecretAgreement(brokerEcdh.PublicKey);
        var handshakeKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            sharedSecret,
            32, // AES-256
            nonce,
            Encoding.UTF8.GetBytes("broker-handshake-v1"));
        CryptographicOperations.ZeroMemory(sharedSecret);

        // 5. Encrypt plaintext with handshake key
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        var aad = Encoding.UTF8.GetBytes(clientPubBase64 + "/api/v1/sessions/register");

        using (var aes = new AesGcm(handshakeKey, 16))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);
        }
        CryptographicOperations.ZeroMemory(handshakeKey);

        // 6. Build encrypted request
        var encryptedReq = new
        {
            v = 1,
            client_ephemeral_pub = clientPubBase64,
            envelope = new
            {
                v = 1,
                alg = "ECDH-ES+A256GCM",
                seq = 0,
                nonce = nonceBase64,
                ciphertext = Convert.ToBase64String(ciphertext),
                tag = Convert.ToBase64String(tag)
            }
        };

        // 7. Send to broker
        var reqJson = JsonSerializer.Serialize(encryptedReq, JsonOpts);
        var content = new StringContent(reqJson, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}/api/v1/sessions/register", content, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Session register failed ({resp.StatusCode}): {respBody}");

        // 8. Parse encrypted response
        var respDoc = JsonDocument.Parse(respBody);

        // Extract session_id from response (plaintext field on handshake response)
        SessionId = respDoc.RootElement.TryGetProperty("session_id", out var sid)
            ? sid.GetString()
            : null;

        if (string.IsNullOrEmpty(SessionId))
            throw new Exception("No session_id in handshake response");

        // 9. Derive session key: ECDH shared secret + HKDF(salt=session_id, info="broker-session-v1")
        var sharedSecret2 = _clientEcdh.DeriveRawSecretAgreement(brokerEcdh.PublicKey);
        _sessionKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            sharedSecret2,
            32,
            Encoding.UTF8.GetBytes(SessionId),
            Encoding.UTF8.GetBytes("broker-session-v1"));
        CryptographicOperations.ZeroMemory(sharedSecret2);

        // 10. Decrypt the response envelope to get scoped_token
        if (respDoc.RootElement.TryGetProperty("envelope", out var respEnvelope))
        {
            var respNonce = Convert.FromBase64String(respEnvelope.GetProperty("nonce").GetString()!);
            var respCiphertext = Convert.FromBase64String(respEnvelope.GetProperty("ciphertext").GetString()!);
            var respTag = Convert.FromBase64String(respEnvelope.GetProperty("tag").GetString()!);
            var respSeq = respEnvelope.TryGetProperty("seq", out var seqProp) ? seqProp.GetInt32() : 0;

            var respAad = Encoding.UTF8.GetBytes($"resp:{SessionId}{respSeq}/api/v1/sessions/register");
            var respPlaintext = new byte[respCiphertext.Length];

            using (var aes2 = new AesGcm(_sessionKey, 16))
            {
                aes2.Decrypt(respNonce, respCiphertext, respTag, respPlaintext, respAad);
            }

            var decryptedResp = Encoding.UTF8.GetString(respPlaintext);
            var respData = JsonDocument.Parse(decryptedResp);

            // Extract scoped_token from the decrypted response
            if (respData.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("scoped_token", out var token))
            {
                ScopedToken = token.GetString();
            }
            else if (respData.RootElement.TryGetProperty("scoped_token", out var token2))
            {
                ScopedToken = token2.GetString();
            }
        }

        if (string.IsNullOrEmpty(ScopedToken))
            throw new Exception("Failed to obtain scoped_token from session registration");

        _seq = 1;
    }

    /// <summary>Execute a capability through the broker</summary>
    public async Task<string> ExecuteAsync(string capabilityId, string route,
        string payloadJson, CancellationToken ct)
    {
        if (_sessionKey == null || SessionId == null || ScopedToken == null)
            throw new InvalidOperationException("Session not established");

        var seq = Interlocked.Increment(ref _seq);
        var idempotencyKey = $"{SessionId}-{seq}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        // Build plaintext JSON without double-serializing the payload
        var payloadNode = JsonNode.Parse(payloadJson);
        var requestObj = new JsonObject
        {
            ["scoped_token"] = ScopedToken,
            ["capability_id"] = capabilityId,
            ["intent"] = $"Execute {route}",
            ["payload"] = payloadNode,
            ["idempotency_key"] = idempotencyKey
        };
        var plaintext = requestObj.ToJsonString();

        var path = "/api/v1/execution-requests/submit";
        var encrypted = EncryptRequest(plaintext, seq, path);

        var reqJson = JsonSerializer.Serialize(new
        {
            v = 1,
            session_id = SessionId,
            envelope = new
            {
                v = 1,
                alg = "A256GCM",
                seq,
                nonce = Convert.ToBase64String(encrypted.Nonce),
                ciphertext = Convert.ToBase64String(encrypted.Ciphertext),
                tag = Convert.ToBase64String(encrypted.Tag)
            }
        }, JsonOpts);

        var content = new StringContent(reqJson, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}{path}", content, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return $"Error ({resp.StatusCode}): {respBody}";

        // Decrypt response
        try
        {
            var respDoc = JsonDocument.Parse(respBody);
            if (respDoc.RootElement.TryGetProperty("envelope", out var respEnvelope))
            {
                var respNonce = Convert.FromBase64String(respEnvelope.GetProperty("nonce").GetString()!);
                var respCiphertext = Convert.FromBase64String(respEnvelope.GetProperty("ciphertext").GetString()!);
                var respTag = Convert.FromBase64String(respEnvelope.GetProperty("tag").GetString()!);
                var respSeq = respEnvelope.TryGetProperty("seq", out var sp) ? sp.GetInt32() : seq;

                var respAad = Encoding.UTF8.GetBytes($"resp:{SessionId}{respSeq}{path}");
                var respPlaintext = new byte[respCiphertext.Length];

                using var aes = new AesGcm(_sessionKey!, 16);
                aes.Decrypt(respNonce, respCiphertext, respTag, respPlaintext, respAad);
                return Encoding.UTF8.GetString(respPlaintext);
            }
        }
        catch (Exception ex)
        {
            return $"Decrypt error: {ex.Message} | Raw: {respBody}";
        }

        return respBody;
    }

    private async Task<string> GetBrokerPubKeyAsync(CancellationToken ct)
    {
        // Try to get from a known endpoint or use env var
        var envKey = Environment.GetEnvironmentVariable("BROKER_PUB_KEY");
        if (!string.IsNullOrEmpty(envKey)) return envKey;

        // Try the health endpoint which might expose it, or just call register
        // without encryption and parse the error for the public key.
        // In practice, the broker public key is distributed out-of-band.
        // For development, we can try to get it by calling a mock endpoint.

        // Fallback: call /api/v1/health and try to extract from response
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/api/v1/health", ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(body);
            // Try snake_case and camelCase
            if (doc.RootElement.TryGetProperty("broker_public_key", out var pk))
                return pk.GetString()!;
            if (doc.RootElement.TryGetProperty("brokerPublicKey", out var pk2))
                return pk2.GetString()!;
        }
        catch { }

        // Last resort: try to register without encryption to get the key from error
        // This won't work if encryption is mandatory. We need the key from config.
        throw new Exception(
            "Cannot determine broker public key. Set BROKER_PUB_KEY environment variable.\n" +
            "Get it from broker startup logs: 'Broker public key generated...'");
    }

    private (byte[] Nonce, byte[] Ciphertext, byte[] Tag) EncryptRequest(string plaintext, int seq, string path)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        var aad = Encoding.UTF8.GetBytes($"req:{SessionId}{seq}{path}");

        using var aes = new AesGcm(_sessionKey!, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);

        return (nonce, ciphertext, tag);
    }
}
