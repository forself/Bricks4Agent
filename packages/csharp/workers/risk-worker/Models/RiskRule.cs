namespace RiskWorker.Models;

/// <summary>
/// 風控規則定義。
/// </summary>
public class RiskRule
{
    public string RuleId      { get; set; } = string.Empty;
    public string Name        { get; set; } = string.Empty;
    public string Type        { get; set; } = string.Empty; // "max_position","max_loss","max_drawdown","max_order_size","max_daily_trades"
    public string? Symbol     { get; set; }                  // null = 全域規則
    public string? Exchange   { get; set; }
    public decimal Threshold  { get; set; }
    public bool Enabled       { get; set; } = true;

    /// <summary>
    /// 規則作用範圍：
    ///   null/"any" = spot 跟 perp 兩條路徑都套（向後相容預設、所有舊規則的行為）
    ///   "spot"     = 只在 RiskEngine.Check() 路徑套
    ///   "perp"     = 只在 RiskEngine.CheckPerp() 路徑套
    /// 用來修 r14 (perp max_loss_per_trade_pct) 跟 r17 (spot max_loss_per_trade_pct)
    /// 同 Type 但分屬不同帳戶類型、舊版會在 spot 路徑同時觸發兩條、violations 列表出現重複訊息。
    /// </summary>
    public string? Scope      { get; set; }

    /// <summary>
    /// 規則特化參數（JSON）。對 threshold 不夠用的規則類型用——例如 time_window 需要
    /// start_hm / end_hm 兩個欄位。null 表示這條規則不需要額外參數。
    /// 各 rule type 自己定義 schema、解析失敗時規則 silently 跳過（不爆 broker）。
    /// </summary>
    public string? Params     { get; set; }
}

/// <summary>
/// 風控檢查結果。
/// </summary>
public class RiskCheckResult
{
    public bool Passed         { get; set; }
    public string OrderAction  { get; set; } = string.Empty; // "allow" | "reject" | "reduce"
    public decimal? AdjustedQty { get; set; }
    public List<RiskViolation> Violations { get; set; } = new();
    public DateTime CheckedAt  { get; set; } = DateTime.UtcNow;
}

public class RiskViolation
{
    public string RuleId    { get; set; } = string.Empty;
    public string RuleName  { get; set; } = string.Empty;
    public string Message   { get; set; } = string.Empty;
    public decimal Current  { get; set; }
    public decimal Limit    { get; set; }
}
