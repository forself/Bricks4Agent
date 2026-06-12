namespace BrokerCore.Services;

/// <summary>
/// 瀏覽器動作分級。階梯由低到高，runtime 不得執行高於授權上限的動作。
/// 對應 BrowserActionAndApprovalModel.md 的 max_action_level。
/// </summary>
public enum BrowserActionLevel
{
    Read = 0,
    Navigate = 1,
    Authenticate = 2,
    DraftAction = 3,
    CommittedAction = 4
}

public static class BrowserActionLevels
{
    public static string ToWire(BrowserActionLevel level) => level switch
    {
        BrowserActionLevel.Read => "read",
        BrowserActionLevel.Navigate => "navigate",
        BrowserActionLevel.Authenticate => "authenticate",
        BrowserActionLevel.DraftAction => "draft_action",
        BrowserActionLevel.CommittedAction => "committed_action",
        _ => "read"
    };

    /// <summary>解析 wire 值；無法辨識或空字串時回傳 null（呼叫端決定保守預設）。</summary>
    public static BrowserActionLevel? TryParse(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire))
            return null;

        return wire.Trim().ToLowerInvariant() switch
        {
            "read" => BrowserActionLevel.Read,
            "navigate" => BrowserActionLevel.Navigate,
            "authenticate" => BrowserActionLevel.Authenticate,
            "draft_action" => BrowserActionLevel.DraftAction,
            "committed_action" => BrowserActionLevel.CommittedAction,
            _ => null
        };
    }
}

public enum BrowserActionDecisionKind
{
    /// <summary>允許執行該 level。</summary>
    Allow,
    /// <summary>意圖 level 超過授權上限，拒絕。</summary>
    ExceedsMaxLevel,
    /// <summary>該 level 需要人工確認，runtime 不得自行執行。</summary>
    RequiresHumanConfirmation,
    /// <summary>意圖 level 無法辨識。</summary>
    UnknownIntendedLevel
}

public sealed record BrowserActionDecision(
    BrowserActionDecisionKind Kind,
    BrowserActionLevel IntendedLevel,
    BrowserActionLevel MaxLevel,
    string Reason)
{
    public bool IsAllowed => Kind == BrowserActionDecisionKind.Allow;
}

/// <summary>
/// 動作分級閘控。確定性、純函數，runtime 在執行任何瀏覽器動作前必須先過閘。
///
/// 保守預設原則：當 broker 未附上 max_action_level（policy context 缺失）時，
/// 上限視為 read —— runtime 絕不因為缺少政策就放寬權限。
/// </summary>
public static class BrowserActionGate
{
    public static BrowserActionDecision Evaluate(
        string? intendedActionLevel,
        string? maxActionLevel,
        IReadOnlyCollection<string>? requiresHumanConfirmationOn)
    {
        // 意圖未指定時，預設為最低的 read。
        var intended = BrowserActionLevels.TryParse(intendedActionLevel) ?? BrowserActionLevel.Read;

        // 缺少政策時，上限保守地視為 read。
        var parsedMax = BrowserActionLevels.TryParse(maxActionLevel);
        if (string.IsNullOrWhiteSpace(maxActionLevel))
        {
            parsedMax = BrowserActionLevel.Read;
        }
        else if (parsedMax == null)
        {
            // 提供了 max 但無法辨識 —— 同樣保守視為 read，並在原因中說明。
            return new BrowserActionDecision(
                BrowserActionDecisionKind.ExceedsMaxLevel,
                intended,
                BrowserActionLevel.Read,
                $"unrecognized max_action_level '{maxActionLevel}', defaulted to read");
        }

        var max = parsedMax.Value;

        if (BrowserActionLevels.TryParse(intendedActionLevel) == null &&
            !string.IsNullOrWhiteSpace(intendedActionLevel))
        {
            return new BrowserActionDecision(
                BrowserActionDecisionKind.UnknownIntendedLevel,
                BrowserActionLevel.Read,
                max,
                $"unrecognized intended_action_level '{intendedActionLevel}'");
        }

        if (intended > max)
        {
            return new BrowserActionDecision(
                BrowserActionDecisionKind.ExceedsMaxLevel,
                intended,
                max,
                $"intended level {BrowserActionLevels.ToWire(intended)} exceeds max {BrowserActionLevels.ToWire(max)}");
        }

        if (requiresHumanConfirmationOn != null && requiresHumanConfirmationOn.Count > 0)
        {
            var intendedWire = BrowserActionLevels.ToWire(intended);
            foreach (var entry in requiresHumanConfirmationOn)
            {
                if (string.Equals(entry?.Trim(), intendedWire, StringComparison.OrdinalIgnoreCase))
                {
                    return new BrowserActionDecision(
                        BrowserActionDecisionKind.RequiresHumanConfirmation,
                        intended,
                        max,
                        $"level {intendedWire} requires human confirmation");
                }
            }
        }

        return new BrowserActionDecision(
            BrowserActionDecisionKind.Allow,
            intended,
            max,
            $"level {BrowserActionLevels.ToWire(intended)} within max {BrowserActionLevels.ToWire(max)}");
    }
}
