using BrokerCore.Models;

namespace BrokerCore.Contracts;

/// <summary>政策裁決結果</summary>
public class PolicyResult
{
    public PolicyDecision Decision { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool Retryable { get; set; }

    /// <summary>RequireApproval 時:該由哪一層批(§18.2)。預設 Admin。</summary>
    public ApproverTier RequiredApproverTier { get; set; } = ApproverTier.Admin;

    public static PolicyResult Allow()
        => new() { Decision = PolicyDecision.Allow, Reason = "Approved by policy engine" };

    public static PolicyResult Deny(string reason, bool retryable = false)
        => new() { Decision = PolicyDecision.Deny, Reason = reason, Retryable = retryable };

    public static PolicyResult RequireApproval(string reason, ApproverTier tier = ApproverTier.Admin)
        => new() { Decision = PolicyDecision.RequireApproval, Reason = reason, RequiredApproverTier = tier };
}
