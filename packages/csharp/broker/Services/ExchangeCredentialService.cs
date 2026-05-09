using BrokerCore.Crypto;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

/// <summary>
/// 用戶 exchange API 憑證的 CRUD + 加密解密。
/// 加密 AAD 綁 entry_id + exchange、防密文被搬到別筆紀錄；
/// 解密失敗（master key 換了 / AAD 被改）會 throw、上層當「資料毀損 / 篡改」處理。
/// </summary>
public sealed class ExchangeCredentialService
{
    private static readonly string[] AllowedExchanges = { "bingx", "binance", "alpaca" };

    private readonly BrokerDb _db;
    private readonly AtRestSecretCrypto _crypto;
    private readonly ILogger<ExchangeCredentialService> _logger;

    public ExchangeCredentialService(BrokerDb db, AtRestSecretCrypto crypto, ILogger<ExchangeCredentialService> logger)
    {
        _db = db;
        _crypto = crypto;
        _logger = logger;
    }

    public sealed class CredentialView
    {
        public string EntryId { get; set; } = "";
        public string OwnerPrincipalId { get; set; } = "";
        public string Exchange { get; set; } = "";
        public string? Label { get; set; }
        public bool IsDemo { get; set; }
        public bool Disabled { get; set; }
        public string ApiKeyMasked { get; set; } = "";        // 顯示前 6 + 後 4 + 中間 ***
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
    }

    public sealed class DecryptedCredential
    {
        public string EntryId { get; set; } = "";
        public string OwnerPrincipalId { get; set; } = "";
        public string Exchange { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string ApiSecret { get; set; } = "";
        public bool IsDemo { get; set; }
    }

    public List<CredentialView> ListForViewer(string? viewerPrincipalId, bool isAdmin)
    {
        // admin 看全部；非 admin 只看自己
        var rows = _db.GetAll<ExchangeCredential>();
        if (!isAdmin && viewerPrincipalId != null)
            rows = rows.Where(r => r.OwnerPrincipalId == viewerPrincipalId).ToList();
        return rows.Select(ToView).ToList();
    }

    public CredentialView? Create(string ownerPid, string exchange, string apiKey, string apiSecret,
        string? label, bool isDemo)
    {
        if (!AllowedExchanges.Contains(exchange.ToLowerInvariant()))
            throw new InvalidOperationException($"Unsupported exchange '{exchange}'. Allowed: {string.Join(", ", AllowedExchanges)}");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 16) throw new InvalidOperationException("API key looks too short");
        if (string.IsNullOrWhiteSpace(apiSecret) || apiSecret.Length < 16) throw new InvalidOperationException("API secret looks too short");

        // 同一 user 同一 exchange 同一 isDemo 只允許一筆 active——簡化用戶心智
        var dup = _db.GetAll<ExchangeCredential>()
            .Any(r => r.OwnerPrincipalId == ownerPid
                   && r.Exchange.Equals(exchange, StringComparison.OrdinalIgnoreCase)
                   && r.IsDemo == isDemo
                   && !r.Disabled);
        if (dup)
            throw new InvalidOperationException($"You already have a {(isDemo ? "demo" : "live")} {exchange} credential. Delete or disable it before adding another.");

        var entryId = $"{ownerPid}:{exchange}:{Guid.NewGuid():N}"[..Math.Min(64, ownerPid.Length + exchange.Length + 33)];
        var aad = $"credential:{entryId}:{exchange}";
        var entry = new ExchangeCredential
        {
            EntryId = entryId,
            OwnerPrincipalId = ownerPid,
            Exchange = exchange.ToLowerInvariant(),
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            ApiKeyEnc = _crypto.Encrypt(apiKey, aad),
            ApiSecretEnc = _crypto.Encrypt(apiSecret, aad),
            IsDemo = isDemo,
        };
        _db.Insert(entry);
        _logger.LogInformation("ExchangeCredential created: owner={Owner} exchange={Ex} demo={Demo} label={Label}",
            ownerPid, exchange, isDemo, label);
        return ToView(entry);
    }

    public bool Delete(string entryId, string requesterPid, bool isAdmin)
    {
        var existing = _db.Get<ExchangeCredential>(entryId);
        if (existing == null) return false;
        if (!isAdmin && existing.OwnerPrincipalId != requesterPid)
            throw new UnauthorizedAccessException("Not your credential");
        _db.Delete<ExchangeCredential>(entryId);
        _logger.LogInformation("ExchangeCredential deleted: {EntryId}", entryId);
        return true;
    }

    public bool SetDisabled(string entryId, bool disabled, string requesterPid, bool isAdmin)
    {
        var existing = _db.Get<ExchangeCredential>(entryId);
        if (existing == null) return false;
        if (!isAdmin && existing.OwnerPrincipalId != requesterPid)
            throw new UnauthorizedAccessException("Not your credential");
        existing.Disabled = disabled;
        existing.UpdatedAt = DateTime.UtcNow;
        _db.Update(existing);
        return true;
    }

    /// <summary>解密一筆 credential、給 trading flow 用。沒找到回 null。標 LastUsedAt。</summary>
    public DecryptedCredential? Resolve(string ownerPid, string exchange, bool? preferDemo = null)
    {
        var rows = _db.GetAll<ExchangeCredential>()
            .Where(r => r.OwnerPrincipalId == ownerPid
                     && r.Exchange.Equals(exchange, StringComparison.OrdinalIgnoreCase)
                     && !r.Disabled)
            .ToList();
        if (rows.Count == 0) return null;
        // 偏好 isDemo 是的優先（A2.5b 之後可能用得到）
        var pick = preferDemo.HasValue
            ? rows.FirstOrDefault(r => r.IsDemo == preferDemo.Value) ?? rows.First()
            : rows.First();

        try
        {
            var aad = $"credential:{pick.EntryId}:{pick.Exchange}";
            var key = _crypto.Decrypt(pick.ApiKeyEnc, aad);
            var sec = _crypto.Decrypt(pick.ApiSecretEnc, aad);
            pick.LastUsedAt = DateTime.UtcNow;
            _db.Update(pick);
            return new DecryptedCredential
            {
                EntryId = pick.EntryId, OwnerPrincipalId = pick.OwnerPrincipalId, Exchange = pick.Exchange,
                ApiKey = key, ApiSecret = sec, IsDemo = pick.IsDemo,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt credential {EntryId} — master key changed or DB tampered?", pick.EntryId);
            return null;
        }
    }

    private static CredentialView ToView(ExchangeCredential e) => new()
    {
        EntryId = e.EntryId, OwnerPrincipalId = e.OwnerPrincipalId, Exchange = e.Exchange,
        Label = e.Label, IsDemo = e.IsDemo, Disabled = e.Disabled,
        ApiKeyMasked = MaskKeyMetadata(e.EntryId),  // 不解密、只顯示「有設」訊號
        CreatedAt = e.CreatedAt, UpdatedAt = e.UpdatedAt, LastUsedAt = e.LastUsedAt,
    };

    private static string MaskKeyMetadata(string entryId)
    {
        // 不顯示真 key——避免不必要的解密。只顯示「••••••(set)」
        return "••••••••(encrypted)";
    }
}
