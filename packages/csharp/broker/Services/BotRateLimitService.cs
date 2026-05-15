using System.Collections.Concurrent;

namespace Broker.Services;

/// <summary>
/// W14 P2 — 內部 bot 呼叫速率限制（per-user sliding window）
///
/// 防護情境：Discord 帳號被盜、攻擊者用 bot 高頻試攻擊 broker（一秒幾百則 message）。
/// 即使 ACL 阻擋 trading.order、攻擊者也可能：
///   - 灌爆 LLM proxy（燒錢）
///   - 灌爆 web search / RAG（燒外部 API quota）
///   - 把 broker CPU 拉滿（DoS）
///
/// 解法：bot-node 在 forward 每則 message 時、設 X-Bot-User-Id header（Discord user ID）。
/// broker 維護 per-user sliding window、超過上限直接 429 + 寫 audit。
/// 沒帶 header 的 fallback 共用「unknown」桶——也限速、避免攻擊者刻意不帶。
///
/// 設計：sliding window log（不用 token bucket）——簡單、容易解釋給答辯。
/// 視窗 60 秒、上限 30 次（每秒 0.5 次平均、瞬間爆衝最多 30 次連發）。
/// 視窗外的 timestamp lazy 清理（每次 check 時順手清掉、無需背景 task）。
/// </summary>
public class BotRateLimitService
{
    public const string UserIdHeader = "X-Bot-User-Id";

    private readonly int _windowSeconds;
    private readonly int _maxRequests;
    private readonly ConcurrentDictionary<string, UserBucket> _buckets = new();

    public BotRateLimitService(IConfiguration cfg)
    {
        _windowSeconds = cfg.GetValue("Bot:RateLimit:WindowSeconds", 60);
        _maxRequests   = cfg.GetValue("Bot:RateLimit:MaxRequests", 30);
    }

    public int WindowSeconds => _windowSeconds;
    public int MaxRequests => _maxRequests;

    /// <summary>
    /// 試圖記一次呼叫。回 (allowed, currentCount)。
    /// allowed=false 表示打到上限、caller 應該 reject。
    /// </summary>
    public (bool Allowed, int Count) TryRecord(string userKey)
    {
        var key = string.IsNullOrWhiteSpace(userKey) ? "(unknown)" : userKey;
        var bucket = _buckets.GetOrAdd(key, _ => new UserBucket());
        var now = DateTime.UtcNow;
        var cutoff = now.AddSeconds(-_windowSeconds);

        lock (bucket.Lock)
        {
            // 清掉視窗外的舊紀錄
            while (bucket.Hits.Count > 0 && bucket.Hits.Peek() < cutoff)
                bucket.Hits.Dequeue();

            if (bucket.Hits.Count >= _maxRequests)
                return (false, bucket.Hits.Count);

            bucket.Hits.Enqueue(now);
            return (true, bucket.Hits.Count);
        }
    }

    /// <summary>給 dashboard / 監控看當前 buckets 狀態。</summary>
    public IReadOnlyDictionary<string, int> Snapshot()
    {
        var result = new Dictionary<string, int>();
        var cutoff = DateTime.UtcNow.AddSeconds(-_windowSeconds);
        foreach (var (key, bucket) in _buckets)
        {
            lock (bucket.Lock)
            {
                while (bucket.Hits.Count > 0 && bucket.Hits.Peek() < cutoff)
                    bucket.Hits.Dequeue();
                if (bucket.Hits.Count > 0) result[key] = bucket.Hits.Count;
            }
        }
        return result;
    }

    private sealed class UserBucket
    {
        public readonly object Lock = new();
        public readonly Queue<DateTime> Hits = new();
    }
}
