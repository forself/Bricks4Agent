using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>任務分類與路由</summary>
public interface ITaskRouter
{
    /// <summary>根據任務類型推斷風險等級</summary>
    RiskLevel AssessRisk(string taskType, string scopeDescriptor);

    /// <summary>根據任務類型推薦角色</summary>
    string? RecommendRole(string taskType);
}
