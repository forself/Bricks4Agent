using BrokerCore.Crypto;

namespace BrokerCore.Services;

/// <summary>
/// Session 存活時間設定（設定區段 <c>Broker:Session</c>）。
///
/// - <see cref="TtlMinutes"/>：註冊時與每次 heartbeat 後，session 從當下起算的有效時間。
///   必須小於 <see cref="CacheSessionKeyStore.DefaultTtl"/>，快取模式下的序號才不會先於 session 失效。
/// - <see cref="MaxLifetimeMinutes"/>：從註冊起算的最長存活時間。heartbeat 不會把 session 延長超過這個時間，
///   到期後必須重新註冊。
/// </summary>
public sealed class SessionLifetimeOptions
{
    public const string SectionName = "Broker:Session";
    public const int DefaultTtlMinutes = 60;
    public const int DefaultMaxLifetimeMinutes = 24 * 60;

    public int TtlMinutes { get; set; } = DefaultTtlMinutes;

    public int MaxLifetimeMinutes { get; set; } = DefaultMaxLifetimeMinutes;

    public TimeSpan Ttl => TimeSpan.FromMinutes(TtlMinutes);

    public TimeSpan MaxLifetime => TimeSpan.FromMinutes(MaxLifetimeMinutes);

    /// <summary>設定不合理時丟出 <see cref="ArgumentOutOfRangeException"/>，讓 broker 在啟動時就失敗。</summary>
    public void Validate()
    {
        if (TtlMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TtlMinutes), TtlMinutes, $"{SectionName}:TtlMinutes must be greater than zero.");
        }

        if (Ttl >= CacheSessionKeyStore.DefaultTtl)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TtlMinutes),
                TtlMinutes,
                $"{SectionName}:TtlMinutes must be less than {CacheSessionKeyStore.DefaultTtl.TotalMinutes:0} minutes (the session key cache lifetime).");
        }

        if (MaxLifetimeMinutes < TtlMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxLifetimeMinutes),
                MaxLifetimeMinutes,
                $"{SectionName}:MaxLifetimeMinutes must be at least {SectionName}:TtlMinutes.");
        }
    }
}
