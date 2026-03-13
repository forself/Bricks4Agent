using BrokerCore.Data;
using BrokerCore.Models;
using CacheClient;

namespace BrokerCore.Services;

/// <summary>
/// 快取版撤權 + Epoch 服務（Phase 2）
///
/// 增強原 RevocationService：
/// - GetCurrentEpoch: 快取優先（本地 0 延遲） → miss 時 DB
/// - IncrementEpoch: DB 原子更新 → 快取 SET → Pub/Sub 廣播
///
/// 快取鍵映射：
/// - sys:epoch → int (current_epoch)
///
/// Pub/Sub：
/// - epoch_changed 頻道：epoch 遞增時廣播給所有 broker
/// - 每個 broker 的 CacheRevocationService 訂閱此頻道 → 本地快取即時失效
///
/// 設計原則：
/// - epoch 讀取必須即時一致（使用 GetStrongAsync 從 Leader 讀取）
/// - 寫入走 DB → 快取 → Pub/Sub（確保持久化 + 即時通知）
/// </summary>
public class CacheRevocationService : IRevocationService
{
    private readonly BrokerDb _db;
    private readonly IDistributedCache _cache;

    // 本地快取（零延遲讀取）
    private volatile int _localEpoch;
    private DateTime _localExpiry = DateTime.MinValue;
    private readonly object _epochLock = new();
    private const int LocalTtlSeconds = 2; // 比 DB 版更短（有 pub/sub 主動推送）

    // 快取鍵
    private const string EpochKey = "sys:epoch";
    private const string EpochChannel = "epoch_changed";

    public CacheRevocationService(BrokerDb db, IDistributedCache cache)
    {
        _db = db;
        _cache = cache;

        // 訂閱 epoch 變更通知
        _ = SubscribeEpochChangesAsync();
    }

    /// <inheritdoc />
    public Revocation Revoke(RevocationTargetType targetType, string targetId, string reason, string revokedBy)
    {
        var revocation = new Revocation
        {
            RevocationId = IdGen.New("rev"),
            TargetTypeValue = (int)targetType,
            TargetId = targetId,
            Reason = reason,
            RevokedBy = revokedBy,
            RevokedAt = DateTime.UtcNow
        };

        _db.Insert(revocation);
        return revocation;
    }

    /// <inheritdoc />
    public bool IsRevoked(string targetId)
    {
        var count = _db.Scalar<int>(
            "SELECT COUNT(1) FROM revocations WHERE target_id = @tid",
            new { tid = targetId });

        return count > 0;
    }

    /// <inheritdoc />
    public int GetCurrentEpoch()
    {
        // 本地快取（0 延遲）
        lock (_epochLock)
        {
            if (DateTime.UtcNow < _localExpiry)
                return _localEpoch;
        }

        // 嘗試從快取讀取（強一致：從 Leader 讀）
        try
        {
            var cached = _cache.GetStrongAsync<int>(EpochKey)
                .GetAwaiter().GetResult();

            if (cached > 0)
            {
                UpdateLocalCache(cached);
                return cached;
            }
        }
        catch
        {
            // 快取不可用
        }

        // DB fallback
        var epoch = _db.Scalar<int>(
            "SELECT current_epoch FROM system_epoch WHERE epoch_id = 1");

        UpdateLocalCache(epoch);

        // 回填快取
        _ = _cache.SetAsync(EpochKey, epoch);

        return epoch;
    }

    /// <inheritdoc />
    public int IncrementEpoch(string triggeredBy, string reason)
    {
        int newEpoch = 0;

        // 1. DB 原子更新（持久化保證）
        _db.InTransaction(() =>
        {
            _db.Execute(
                "UPDATE system_epoch SET current_epoch = current_epoch + 1, updated_at = @now, updated_by = @by WHERE epoch_id = 1",
                new { now = DateTime.UtcNow, by = triggeredBy });

            var epoch = _db.Scalar<int>(
                "SELECT current_epoch FROM system_epoch WHERE epoch_id = 1");
            newEpoch = epoch;

            Revoke(RevocationTargetType.Token, $"epoch_{epoch}", reason, triggeredBy);
        });

        // 2. 更新快取
        _ = _cache.SetAsync(EpochKey, newEpoch);

        // 3. 更新本地快取
        UpdateLocalCache(newEpoch);

        // 4. Pub/Sub 廣播給所有 broker 實例
        _ = _cache.PublishAsync(EpochChannel, newEpoch.ToString());

        return newEpoch;
    }

    /// <summary>更新本地快取</summary>
    private void UpdateLocalCache(int epoch)
    {
        lock (_epochLock)
        {
            _localEpoch = epoch;
            _localExpiry = DateTime.UtcNow.AddSeconds(LocalTtlSeconds);
        }
    }

    /// <summary>訂閱 epoch 變更通知</summary>
    private async Task SubscribeEpochChangesAsync()
    {
        try
        {
            await _cache.SubscribeAsync(EpochChannel, (channel, message) =>
            {
                if (int.TryParse(message, out var newEpoch))
                {
                    UpdateLocalCache(newEpoch);
                }
                return Task.CompletedTask;
            });
        }
        catch
        {
            // 訂閱失敗不影響功能（依靠 TTL 失效 + DB fallback）
        }
    }
}
