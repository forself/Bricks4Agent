namespace BrokerCore.Services;

/// <summary>
/// 範圍委派 Token 服務（獨立實作，不依賴 JwtHelper）
/// Claims 模型以 string principal_id 為核心（非 int userId）
/// </summary>
public interface IScopedTokenService
{
    /// <summary>發行 Scoped Token</summary>
    string GenerateToken(ScopedTokenClaims claims);

    /// <summary>驗證並解析 Scoped Token</summary>
    ScopedTokenClaims? ValidateToken(string token);
}

/// <summary>Scoped Token 的 claim 集合</summary>
public class ScopedTokenClaims
{
    /// <summary>主體 ID（string ULID，非 int）</summary>
    public string PrincipalId { get; set; } = string.Empty;

    /// <summary>唯一 Token ID</summary>
    public string Jti { get; set; } = string.Empty;

    /// <summary>任務 ID</summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Session ID</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>角色 ID</summary>
    public string RoleId { get; set; } = string.Empty;

    /// <summary>已授予的能力 ID 清單</summary>
    public string[] CapabilityIds { get; set; } = Array.Empty<string>();

    /// <summary>範圍（JSON）</summary>
    public string Scope { get; set; } = "{}";

    /// <summary>發行時的 system epoch</summary>
    public int Epoch { get; set; }
}
