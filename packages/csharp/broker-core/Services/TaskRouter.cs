using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 任務分類與路由
///
/// Phase 1：基於 task_type 字串的確定性映射
/// Phase 3：ML 輔助分類（僅建議性）
/// </summary>
public class TaskRouter : ITaskRouter
{
    // 任務類型 → 風險等級映射
    private static readonly Dictionary<string, RiskLevel> RiskMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["query"] = RiskLevel.Low,
        ["analysis"] = RiskLevel.Low,
        ["review"] = RiskLevel.Low,
        ["planning"] = RiskLevel.Low,
        ["task_management"] = RiskLevel.Low,
        ["architecture"] = RiskLevel.Low,
        ["code_gen"] = RiskLevel.Medium,
        ["doc_gen"] = RiskLevel.Medium,
        ["code_modify"] = RiskLevel.High,
        ["deployment"] = RiskLevel.Critical,
        ["admin"] = RiskLevel.Critical
    };

    // 任務類型 → 推薦角色映射
    private static readonly Dictionary<string, string> RoleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["query"] = "role_reader",
        ["analysis"] = "role_reader",
        ["review"] = "role_sa",
        ["planning"] = "role_pm",
        ["task_management"] = "role_pm",
        ["architecture"] = "role_sa",
        ["code_gen"] = "role_executor",
        ["doc_gen"] = "role_executor",
        ["audit"] = "role_auditor",
        ["admin"] = "role_admin"
    };

    /// <inheritdoc />
    public RiskLevel AssessRisk(string taskType, string scopeDescriptor)
    {
        if (RiskMap.TryGetValue(taskType, out var level))
            return level;

        // 未知類型 → Medium（保守）
        return RiskLevel.Medium;
    }

    /// <inheritdoc />
    public string? RecommendRole(string taskType)
    {
        return RoleMap.TryGetValue(taskType, out var role) ? role : null;
    }
}
