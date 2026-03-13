using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace BrokerCore.Services;

/// <summary>
/// 範圍委派 Token 實作（獨立於 JwtHelper）
///
/// 設計決策：
/// - Claims 以 string principal_id 為核心（非 JwtHelper 的 int userId）
/// - 包含 task_id、session_id、role_id、capability_ids[]、scope、epoch
/// - HMAC-SHA256 簽章
/// - 短時效（預設 15 分鐘）
///
/// 生命週期：Singleton
/// </summary>
public class ScopedTokenService : IScopedTokenService
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;

    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    // Custom claim 名稱
    private const string ClaimTaskId = "task_id";
    private const string ClaimSessionId = "session_id";
    private const string ClaimRoleId = "role_id";
    private const string ClaimCapabilityIds = "capability_ids";
    private const string ClaimScope = "scope";
    private const string ClaimEpoch = "epoch";

    public ScopedTokenService(string secret, string issuer, string audience, int expirationMinutes = 15)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.StartsWith("CHANGE_ME"))
        {
            // 開發模式：使用隨機密鑰
            var randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            _signingKey = new SymmetricSecurityKey(randomBytes);
        }
        else
        {
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            if (keyBytes.Length < 32)
                throw new ArgumentException("ScopedToken secret must be at least 256 bits (32 bytes).");
            _signingKey = new SymmetricSecurityKey(keyBytes);
        }

        _issuer = issuer;
        _audience = audience;
        _expirationMinutes = expirationMinutes;
    }

    /// <inheritdoc />
    public string GenerateToken(ScopedTokenClaims claims)
    {
        var now = DateTime.UtcNow;

        var tokenClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, claims.PrincipalId),
            new(JwtRegisteredClaimNames.Jti, claims.Jti),
            new(ClaimTaskId, claims.TaskId),
            new(ClaimSessionId, claims.SessionId),
            new(ClaimRoleId, claims.RoleId),
            new(ClaimCapabilityIds, JsonSerializer.Serialize(claims.CapabilityIds)),
            new(ClaimScope, claims.Scope),
            new(ClaimEpoch, claims.Epoch.ToString()),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(tokenClaims),
            Issuer = _issuer,
            Audience = _audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(_expirationMinutes),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256Signature)
        };

        var token = _tokenHandler.CreateToken(descriptor);
        return _tokenHandler.WriteToken(token);
    }

    /// <inheritdoc />
    public ScopedTokenClaims? ValidateToken(string token)
    {
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ClockSkew = TimeSpan.FromSeconds(30) // 極短時鐘偏差
        };

        try
        {
            var principal = _tokenHandler.ValidateToken(token, validationParams, out _);

            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var taskId = principal.FindFirst(ClaimTaskId)?.Value;
            var sessionId = principal.FindFirst(ClaimSessionId)?.Value;
            var roleId = principal.FindFirst(ClaimRoleId)?.Value;
            var capIdsJson = principal.FindFirst(ClaimCapabilityIds)?.Value;
            var scope = principal.FindFirst(ClaimScope)?.Value;
            var epochStr = principal.FindFirst(ClaimEpoch)?.Value;

            if (sub == null || jti == null || taskId == null || sessionId == null || roleId == null)
                return null;

            var capIds = !string.IsNullOrEmpty(capIdsJson)
                ? JsonSerializer.Deserialize<string[]>(capIdsJson) ?? Array.Empty<string>()
                : Array.Empty<string>();

            return new ScopedTokenClaims
            {
                PrincipalId = sub,
                Jti = jti,
                TaskId = taskId,
                SessionId = sessionId,
                RoleId = roleId,
                CapabilityIds = capIds,
                Scope = scope ?? "{}",
                Epoch = int.TryParse(epochStr, out var ep) ? ep : 0
            };
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }
}
