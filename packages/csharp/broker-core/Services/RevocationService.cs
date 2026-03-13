using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 撤權 + System Epoch 服務
///
/// Kill Switch 語意：
/// - epoch 遞增 → 所有 token.epoch &lt; current_epoch 的 Token 即時失效
/// - O(1) 檢查，零時間窗
/// - 不需逐筆撤銷，一次性失效所有舊 token
///
/// 叢集化：
/// - epoch 值存 DB（system_epoch 表，只一行）
/// - 短快取（5 秒 TTL）避免每次請求都查 DB
/// - Phase 2：Redis pub/sub 廣播 epoch 變更
/// </summary>
public class RevocationService : IRevocationService
{
    private readonly BrokerDb _db;

    // 短快取：避免每次請求都查 DB
    private int _cachedEpoch;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private readonly object _epochLock = new();
    private const int CacheTtlSeconds = 5;

    public RevocationService(BrokerDb db)
    {
        _db = db;
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
        lock (_epochLock)
        {
            if (DateTime.UtcNow < _cacheExpiry)
                return _cachedEpoch;

            var epoch = _db.Scalar<int>(
                "SELECT current_epoch FROM system_epoch WHERE epoch_id = 1");

            _cachedEpoch = epoch;
            _cacheExpiry = DateTime.UtcNow.AddSeconds(CacheTtlSeconds);

            return epoch;
        }
    }

    /// <inheritdoc />
    public int IncrementEpoch(string triggeredBy, string reason)
    {
        int newEpoch = 0;

        _db.InTransaction(() =>
        {
            // 原子遞增
            _db.Execute(
                "UPDATE system_epoch SET current_epoch = current_epoch + 1, updated_at = @now, updated_by = @by WHERE epoch_id = 1",
                new { now = DateTime.UtcNow, by = triggeredBy });

            var epoch = _db.Scalar<int>(
                "SELECT current_epoch FROM system_epoch WHERE epoch_id = 1");
            newEpoch = epoch;

            // 記錄撤權事件（epoch 級別）
            Revoke(RevocationTargetType.Token, $"epoch_{epoch}", reason, triggeredBy);
        });

        // 立即刷新快取
        lock (_epochLock)
        {
            _cachedEpoch = newEpoch;
            _cacheExpiry = DateTime.UtcNow.AddSeconds(CacheTtlSeconds);
        }

        return newEpoch;
    }
}
