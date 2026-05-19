using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

/// <summary>
/// 通知 dedup 持久化 repo（防 broker restart 清空 in-memory state、同樣訊息每次重啟都被推）。
///
/// 為什麼需要：DiscordNotificationService + LineNotificationService 內各有
/// `Dictionary<string, DateTime> _errorSignatureLastSentAt`、broker rebuild 一次就忘、
/// 1 個下午 rebuild 7 次 → 同一條 risk-blocked 訊息被推 7+ 次 spam Discord/LINE。
///
/// 用法：兩個 notification service 注入這個 repo、原本 in-memory check 改成 repo check。
/// API 跟 in-memory dict 行為一致（IsRecentlySent + MarkSent）。
///
/// 注意：寫入失敗只 log warning、不影響 caller。Dedup 是優化、不是強制；最差行為退化成
/// in-memory 那版（broker restart 後 spam 一次）、不會比現狀差。
/// </summary>
public class NotificationDedupRepo
{
    private readonly BrokerDb _db;
    private readonly ILogger<NotificationDedupRepo> _logger;

    public NotificationDedupRepo(BrokerDb db, ILogger<NotificationDedupRepo> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 該 (channel, signature) 在 window 內是否已推過？
    /// 回 true = 已推、不該再推；回 false = 沒紀錄或過期、可推。
    /// </summary>
    public bool IsRecentlySent(string channel, string signature, TimeSpan window)
    {
        try
        {
            var key = MakeKey(channel, signature);
            var entry = _db.Get<NotificationDedupEntry>(key);
            if (entry == null) return false;
            return (DateTime.UtcNow - entry.LastSentAt) < window;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NotificationDedupRepo.IsRecentlySent fail (allow push)");
            return false;  // db 出問題就讓推、別讓 dedup 把 alert 全擋掉
        }
    }

    /// <summary>記下 (channel, signature) 剛被推過。</summary>
    public void MarkSent(string channel, string signature)
    {
        try
        {
            var key = MakeKey(channel, signature);
            var existing = _db.Get<NotificationDedupEntry>(key);
            if (existing == null)
            {
                _db.Insert(new NotificationDedupEntry
                {
                    DedupKey = key,
                    Channel = channel,
                    Signature = signature,
                    LastSentAt = DateTime.UtcNow,
                });
            }
            else
            {
                existing.LastSentAt = DateTime.UtcNow;
                _db.Update(existing);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NotificationDedupRepo.MarkSent fail (continue without dedup)");
        }
    }

    /// <summary>清掉 N 天前的紀錄、防表無限變大。背景任務可定時 call。</summary>
    public int PurgeOlderThan(TimeSpan age)
    {
        try
        {
            var cutoff = DateTime.UtcNow - age;
            // BaseOrm 用 parameter binding（SQLite dialect @prefix）
            return _db.Execute(
                "DELETE FROM notification_dedup WHERE last_sent_at < @cutoff",
                new { cutoff });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NotificationDedupRepo.PurgeOlderThan fail");
            return 0;
        }
    }

    private static string MakeKey(string channel, string signature)
    {
        // 長度防呆：signature 最多 180、加 channel 跟 separator 應仍在 200 內
        var truncSig = signature.Length > 180 ? signature[..180] : signature;
        return $"{channel}::{truncSig}";
    }
}
