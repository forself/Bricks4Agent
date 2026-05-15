namespace Broker.Services;

/// <summary>
/// W14 安全加固 P1+P5 — 緊急停機 + 唯讀鎖定狀態旗標（in-memory，broker restart 會清掉）
///
/// 兩個獨立旗標：
///
/// 1. <b>KillSwitch</b>（P1 — 緊急停機）：
///    - 一鍵停掉 AutoTrader、把所有 watch 設成非 active、阻擋 trading.* capability
///    - 用途：Discord/dashboard 帳號被盜下單、市場閃崩、demo 中失控
///    - 由 EmergencyEndpoints 的 POST /emergency/stop-all 觸發
///
/// 2. <b>ReadOnlyMode</b>（P5 — 唯讀鎖定）：
///    - 全 broker 拒絕所有 POST/PUT/DELETE（exempt：emergency/auth/health）
///    - 用途：demo 模式防誤觸、出國一週留 broker 跑但禁寫
///    - 由 EmergencyEndpoints 的 POST /emergency/lockdown 切換
///
/// 為什麼不持久化：broker restart 是極少數場景（只有我手動 deploy）、
/// 重啟後本來就要重新檢查狀態。持久化會讓「不小心鎖死又重啟」變更難復原。
/// </summary>
public interface IEmergencyState
{
    bool KillSwitchActive { get; }
    DateTime? KillSwitchAt { get; }
    string? KillSwitchBy { get; }
    string? KillSwitchReason { get; }

    bool ReadOnlyMode { get; }
    DateTime? ReadOnlyAt { get; }
    string? ReadOnlyBy { get; }
    string? ReadOnlyReason { get; }

    void TriggerKillSwitch(string by, string reason);
    void ClearKillSwitch(string by);
    void SetReadOnly(bool on, string by, string reason);
}

public sealed class EmergencyStateService : IEmergencyState
{
    private readonly object _lock = new();
    private bool _killSwitch;
    private DateTime? _killAt;
    private string? _killBy;
    private string? _killReason;
    private bool _readOnly;
    private DateTime? _roAt;
    private string? _roBy;
    private string? _roReason;

    public bool KillSwitchActive { get { lock (_lock) return _killSwitch; } }
    public DateTime? KillSwitchAt { get { lock (_lock) return _killAt; } }
    public string? KillSwitchBy { get { lock (_lock) return _killBy; } }
    public string? KillSwitchReason { get { lock (_lock) return _killReason; } }

    public bool ReadOnlyMode { get { lock (_lock) return _readOnly; } }
    public DateTime? ReadOnlyAt { get { lock (_lock) return _roAt; } }
    public string? ReadOnlyBy { get { lock (_lock) return _roBy; } }
    public string? ReadOnlyReason { get { lock (_lock) return _roReason; } }

    public void TriggerKillSwitch(string by, string reason)
    {
        lock (_lock)
        {
            _killSwitch = true;
            _killAt = DateTime.UtcNow;
            _killBy = by;
            _killReason = string.IsNullOrWhiteSpace(reason) ? "(no reason given)" : reason;
        }
    }

    public void ClearKillSwitch(string by)
    {
        lock (_lock)
        {
            _killSwitch = false;
            _killAt = null;
            _killBy = $"cleared by {by}";
            _killReason = null;
        }
    }

    public void SetReadOnly(bool on, string by, string reason)
    {
        lock (_lock)
        {
            _readOnly = on;
            _roAt = on ? DateTime.UtcNow : null;
            _roBy = on ? by : $"cleared by {by}";
            _roReason = on ? (string.IsNullOrWhiteSpace(reason) ? "(no reason given)" : reason) : null;
        }
    }
}
