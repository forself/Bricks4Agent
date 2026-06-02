using BrokerCore.Crypto;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

/// <summary>
/// 每個 principal 的推播目標 CRUD + 加密解密。多用戶:朋友收自己的告警 / 每日彙整到自己頻道。
/// target（Discord webhook URL / LINE token）是 secret、AAD 綁 entry_id + type 防搬移。
/// MVP 路由先支援 discord;line 可登記、路由 follow-on。
/// </summary>
public sealed class NotificationChannelService
{
    private static readonly string[] AllowedTypes = { "discord", "line" };

    private readonly BrokerDb _db;
    private readonly AtRestSecretCrypto _crypto;
    private readonly ILogger<NotificationChannelService> _logger;

    public NotificationChannelService(BrokerDb db, AtRestSecretCrypto crypto, ILogger<NotificationChannelService> logger)
    {
        _db = db;
        _crypto = crypto;
        _logger = logger;
    }

    public sealed class ChannelView
    {
        public string EntryId { get; set; } = "";
        public string OwnerPrincipalId { get; set; } = "";
        public string ChannelType { get; set; } = "";
        public string? Label { get; set; }
        public bool Disabled { get; set; }
        public string TargetMasked { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
    }

    public sealed class ResolvedChannel
    {
        public string EntryId { get; set; } = "";
        public string OwnerPrincipalId { get; set; } = "";
        public string ChannelType { get; set; } = "";
        public string Target { get; set; } = "";
    }

    public List<ChannelView> ListForViewer(string? viewerPrincipalId, bool isAdmin)
    {
        var rows = _db.GetAll<NotificationChannel>();
        if (!isAdmin && viewerPrincipalId != null)
            rows = rows.Where(r => r.OwnerPrincipalId == viewerPrincipalId).ToList();
        return rows.Select(ToView).ToList();
    }

    public ChannelView Create(string ownerPid, string channelType, string target, string? label)
    {
        var type = channelType.ToLowerInvariant();
        if (!AllowedTypes.Contains(type))
            throw new InvalidOperationException($"Unsupported channel type '{channelType}'. Allowed: {string.Join(", ", AllowedTypes)}");
        if (string.IsNullOrWhiteSpace(target) || target.Length < 16)
            throw new InvalidOperationException("Target (webhook URL / token) looks too short");
        if (type == "discord" && !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Discord target must be a webhook URL (https://...)");

        // 同 user 同 type 只允許一筆 active——簡化心智
        var dup = _db.GetAll<NotificationChannel>()
            .Any(r => r.OwnerPrincipalId == ownerPid
                   && r.ChannelType.Equals(type, StringComparison.OrdinalIgnoreCase)
                   && !r.Disabled);
        if (dup)
            throw new InvalidOperationException($"You already have an active {type} channel. Delete or disable it before adding another.");

        var entryId = $"{ownerPid}:{type}:{Guid.NewGuid():N}"[..Math.Min(64, ownerPid.Length + type.Length + 33)];
        var aad = $"notify:{entryId}:{type}";
        var entry = new NotificationChannel
        {
            EntryId = entryId,
            OwnerPrincipalId = ownerPid,
            ChannelType = type,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            TargetEnc = _crypto.Encrypt(target, aad),
        };
        _db.Insert(entry);
        _logger.LogInformation("NotificationChannel created: owner={Owner} type={Type} label={Label}", ownerPid, type, label);
        return ToView(entry);
    }

    public bool Delete(string entryId, string requesterPid, bool isAdmin)
    {
        var existing = _db.Get<NotificationChannel>(entryId);
        if (existing == null) return false;
        if (!isAdmin && existing.OwnerPrincipalId != requesterPid)
            throw new UnauthorizedAccessException("Not your channel");
        _db.Delete<NotificationChannel>(entryId);
        _logger.LogInformation("NotificationChannel deleted: {EntryId}", entryId);
        return true;
    }

    public bool SetDisabled(string entryId, bool disabled, string requesterPid, bool isAdmin)
    {
        var existing = _db.Get<NotificationChannel>(entryId);
        if (existing == null) return false;
        if (!isAdmin && existing.OwnerPrincipalId != requesterPid)
            throw new UnauthorizedAccessException("Not your channel");
        existing.Disabled = disabled;
        existing.UpdatedAt = DateTime.UtcNow;
        _db.Update(existing);
        return true;
    }

    /// <summary>解一個 principal 的某 type 推播目標。沒找到回 null。標 LastUsedAt。</summary>
    public ResolvedChannel? Resolve(string ownerPid, string channelType)
    {
        var pick = _db.GetAll<NotificationChannel>()
            .FirstOrDefault(r => r.OwnerPrincipalId == ownerPid
                              && r.ChannelType.Equals(channelType, StringComparison.OrdinalIgnoreCase)
                              && !r.Disabled);
        if (pick == null) return null;
        return Decrypt(pick);
    }

    /// <summary>列出所有 active 的某 type 頻道（給每日彙整逐用戶推播迭代用）。解密失敗的略過。</summary>
    public List<ResolvedChannel> ListActiveByType(string channelType)
    {
        var result = new List<ResolvedChannel>();
        foreach (var r in _db.GetAll<NotificationChannel>()
                     .Where(r => r.ChannelType.Equals(channelType, StringComparison.OrdinalIgnoreCase) && !r.Disabled))
        {
            var dec = Decrypt(r, touch: false);
            if (dec != null) result.Add(dec);
        }
        return result;
    }

    private ResolvedChannel? Decrypt(NotificationChannel pick, bool touch = true)
    {
        try
        {
            var aad = $"notify:{pick.EntryId}:{pick.ChannelType}";
            var target = _crypto.Decrypt(pick.TargetEnc, aad);
            if (touch) { pick.LastUsedAt = DateTime.UtcNow; _db.Update(pick); }
            return new ResolvedChannel
            {
                EntryId = pick.EntryId, OwnerPrincipalId = pick.OwnerPrincipalId,
                ChannelType = pick.ChannelType, Target = target,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt notification channel {EntryId} — master key changed or DB tampered?", pick.EntryId);
            return null;
        }
    }

    private static ChannelView ToView(NotificationChannel e) => new()
    {
        EntryId = e.EntryId, OwnerPrincipalId = e.OwnerPrincipalId, ChannelType = e.ChannelType,
        Label = e.Label, Disabled = e.Disabled,
        TargetMasked = "••••••••(encrypted)",
        CreatedAt = e.CreatedAt, UpdatedAt = e.UpdatedAt, LastUsedAt = e.LastUsedAt,
    };
}
