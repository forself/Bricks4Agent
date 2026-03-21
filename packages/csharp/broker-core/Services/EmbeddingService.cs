using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BrokerCore.Services;

/// <summary>
/// 嵌入向量服務 —— 呼叫 Ollama /api/embed 產生文字向量
/// 支援語意搜尋（cosine similarity）
///
/// 注意：此類位於 BrokerCore（無 Microsoft.Extensions.Logging），
/// 使用 Action delegate 進行日誌輸出
/// </summary>
public class EmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingOptions _options;
    private readonly Action<string>? _log;

    public EmbeddingService(EmbeddingOptions options, Action<string>? log = null)
    {
        _options = options;
        _log = log;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
    }

    public bool IsEnabled => _options.Enabled;
    public string ModelName => _options.Model;
    public int ExpectedDimension => _options.Dimension;

    /// <summary>產生單一文字的嵌入向量</summary>
    public async Task<float[]?> EmbedAsync(string text)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                model = _options.Model,
                input = new[] { text }
            });

            var response = await _httpClient.PostAsync(
                "/api/embed",
                new StringContent(requestBody, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"[Embedding] API returned {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return ParseEmbeddingResponse(json);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Embedding] Failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>批次嵌入多段文字</summary>
    public async Task<float[][]?> EmbedBatchAsync(string[] texts)
    {
        if (!_options.Enabled || texts.Length == 0)
            return null;

        try
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                model = _options.Model,
                input = texts
            });

            var response = await _httpClient.PostAsync(
                "/api/embed",
                new StringContent(requestBody, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("embeddings", out var embeddings) &&
                embeddings.ValueKind == JsonValueKind.Array)
            {
                var results = new float[embeddings.GetArrayLength()][];
                int idx = 0;
                foreach (var emb in embeddings.EnumerateArray())
                {
                    var vector = new float[emb.GetArrayLength()];
                    int i = 0;
                    foreach (var val in emb.EnumerateArray())
                        vector[i++] = val.GetSingle();
                    results[idx++] = vector;
                }
                return results;
            }

            return null;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Embedding] Batch failed: {ex.Message}");
            return null;
        }
    }

    // ═══════════════════════════════════════════
    // 解析
    // ═══════════════════════════════════════════

    private float[]? ParseEmbeddingResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        // Ollama /api/embed 格式: { "embeddings": [[0.1, 0.2, ...]] }
        if (doc.RootElement.TryGetProperty("embeddings", out var embeddings) &&
            embeddings.ValueKind == JsonValueKind.Array &&
            embeddings.GetArrayLength() > 0)
        {
            return JsonArrayToFloats(embeddings[0]);
        }

        // 相容舊版 /api/embeddings: { "embedding": [0.1, 0.2, ...] }
        if (doc.RootElement.TryGetProperty("embedding", out var singleEmb) &&
            singleEmb.ValueKind == JsonValueKind.Array)
        {
            return JsonArrayToFloats(singleEmb);
        }

        _log?.Invoke("[Embedding] Unexpected response format");
        return null;
    }

    private static float[] JsonArrayToFloats(JsonElement array)
    {
        var vector = new float[array.GetArrayLength()];
        int i = 0;
        foreach (var val in array.EnumerateArray())
            vector[i++] = val.GetSingle();
        return vector;
    }

    // ═══════════════════════════════════════════
    // 向量工具函數
    // ═══════════════════════════════════════════

    /// <summary>餘弦相似度（-1 ~ 1，越大越相似）</summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;

        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < 1e-10f ? 0f : dot / denom;
    }

    /// <summary>float[] → byte[]（Little-Endian IEEE 754）</summary>
    public static byte[] VectorToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>byte[] → float[]</summary>
    public static float[] BytesToVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    /// <summary>使用指定模型產生嵌入（覆蓋預設模型）</summary>
    public async Task<float[]?> EmbedWithModelAsync(string text, string model)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                model,
                input = new[] { text }
            });

            var response = await _httpClient.PostAsync(
                "/api/embed",
                new StringContent(requestBody, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"[Embedding] Model '{model}' returned {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return ParseEmbeddingResponse(json);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Embedding] Model '{model}' failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>取得可用模型清單（預設 + 替代）</summary>
    public List<string> GetAvailableModels()
    {
        var models = new List<string> { _options.Model };
        models.AddRange(_options.AlternativeModels);
        return models;
    }

    /// <summary>SHA256 雜湊（用於避免重複嵌入）</summary>
    public static string ComputeHash(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }
}

/// <summary>嵌入服務配置</summary>
public class EmbeddingOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "ollama";
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "nomic-embed-text";
    public int Dimension { get; set; } = 768;
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 可選替代模型清單（多語言/中文最佳化）
    /// 設定後可透過 EmbedAsync(text, modelOverride) 指定使用
    /// 例如：["bge-m3", "multilingual-e5-large"]
    /// </summary>
    public List<string> AlternativeModels { get; set; } = new();
}
