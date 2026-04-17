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
