using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class PortalLineVerificationService
{
    private readonly BrokerDb _db;
    private readonly PortalAuthOptions _options;
    private readonly object _gate = new();

    public PortalLineVerificationService(BrokerDb db, PortalAuthOptions options)
    {
        _db = db;
        _options = options;
    }

    public PortalLineVerificationIssue IssueCode(PortalUserCredential credential)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var expiresAt = DateTime.UtcNow.AddMinutes(Math.Max(1, _options.LineVerificationCodeMinutes));

        lock (_gate)
        {
            credential.LineVerificationCodeHash = ComputeSha256(code);
            credential.LineVerificationCodeExpiresAt = expiresAt;
            credential.UpdatedAt = DateTime.UtcNow;
            _db.Update(credential);
        }

        return new PortalLineVerificationIssue
        {
            UserId = credential.UserId,
            Code = code,
            ExpiresAt = expiresAt,
            Command = BuildVerifyCommand(credential.UserId, code)
        };
    }

    public PortalLineVerificationStatus GetStatus(string userId)
    {
        var credential = _db.Get<PortalUserCredential>(userId);
        if (credential == null)
        {
            return new PortalLineVerificationStatus
            {
                UserId = userId,
                Verified = false
            };
        }

        return ToStatus(credential);
    }

    public string? ResolvePortalUserIdForLineUser(string lineUserId)
    {
        var credential = FindByLineUserId(lineUserId);
        return credential?.UserId;
    }

    public PortalLineVerificationResult Verify(string lineUserId, string userId, string code)
    {
        var normalizedUserId = (userId ?? string.Empty).Trim();
        var normalizedCode = (code ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(lineUserId) ||
            string.IsNullOrWhiteSpace(normalizedUserId) ||
            string.IsNullOrWhiteSpace(normalizedCode))
        {
            return PortalLineVerificationResult.Fail("line_verification_failed", "Usage: /verify <user_id> <code>");
        }

        lock (_gate)
        {
            var existingLineOwner = FindByLineUserId(lineUserId);
            if (existingLineOwner != null &&
                !string.Equals(existingLineOwner.UserId, normalizedUserId, StringComparison.Ordinal))
            {
                return PortalLineVerificationResult.Fail(
                    "line_verification_failed",
                    "This LINE account is already linked to another Portal account.");
            }

            var credential = _db.Get<PortalUserCredential>(normalizedUserId);
            if (credential == null || credential.Disabled)
            {
                return PortalLineVerificationResult.Fail("line_verification_failed", "Portal account not found.");
            }

            if (!string.IsNullOrWhiteSpace(credential.LineUserId) &&
                !string.Equals(credential.LineUserId, lineUserId, StringComparison.Ordinal))
            {
                return PortalLineVerificationResult.Fail(
                    "line_verification_failed",
                    "This Portal account is already linked to another LINE account.");
            }

            if (credential.LineVerifiedAt != null &&
                string.Equals(credential.LineUserId, lineUserId, StringComparison.Ordinal))
            {
                return PortalLineVerificationResult.Ok(
                    credential.UserId,
                    "LINE account is already verified for this Portal account.");
            }

            if (credential.LineVerificationCodeExpiresAt == null ||
                credential.LineVerificationCodeExpiresAt <= DateTime.UtcNow ||
                string.IsNullOrWhiteSpace(credential.LineVerificationCodeHash))
            {
                return PortalLineVerificationResult.Fail(
                    "line_verification_failed",
                    "Verification code is invalid or expired.");
            }

            var actual = ComputeSha256(normalizedCode);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(actual),
                    Encoding.UTF8.GetBytes(credential.LineVerificationCodeHash)))
            {
                return PortalLineVerificationResult.Fail(
                    "line_verification_failed",
                    "Verification code is invalid or expired.");
            }

            credential.LineUserId = lineUserId;
            credential.LineVerifiedAt = DateTime.UtcNow;
            credential.LineVerificationCodeHash = string.Empty;
            credential.LineVerificationCodeExpiresAt = null;
            credential.UpdatedAt = DateTime.UtcNow;
            _db.Update(credential);

            return PortalLineVerificationResult.Ok(
                credential.UserId,
                "LINE verification completed. You can now use Bricks4Agent from LINE.");
        }
    }

    private PortalUserCredential? FindByLineUserId(string lineUserId)
        => _db.Query<PortalUserCredential>(
                "SELECT * FROM portal_user_credentials WHERE line_user_id = @lineUserId LIMIT 1",
                new { lineUserId })
            .FirstOrDefault();

    private static PortalLineVerificationStatus ToStatus(PortalUserCredential credential)
        => new()
        {
            UserId = credential.UserId,
            Verified = credential.LineVerifiedAt != null && !string.IsNullOrWhiteSpace(credential.LineUserId),
            LineUserId = credential.LineUserId,
            ExpiresAt = credential.LineVerificationCodeExpiresAt,
            VerifiedAt = credential.LineVerifiedAt,
            CommandTemplate = "/verify <user_id> <code>"
        };

    private static string BuildVerifyCommand(string userId, string code)
        => $"/verify {userId} {code}";

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed class PortalLineVerificationIssue
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;
}

public sealed class PortalLineVerificationStatus
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("verified")]
    public bool Verified { get; set; }
    [JsonPropertyName("line_user_id")]
    public string LineUserId { get; set; } = string.Empty;
    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }
    [JsonPropertyName("verified_at")]
    public DateTime? VerifiedAt { get; set; }
    [JsonPropertyName("command_template")]
    public string CommandTemplate { get; set; } = string.Empty;
}

public sealed class PortalLineVerificationResult
{
    public bool Success { get; private init; }
    public string Error { get; private init; } = string.Empty;
    public string Message { get; private init; } = string.Empty;
    public string EffectiveUserId { get; private init; } = string.Empty;

    public static PortalLineVerificationResult Ok(string effectiveUserId, string message)
        => new()
        {
            Success = true,
            EffectiveUserId = effectiveUserId,
            Message = message
        };

    public static PortalLineVerificationResult Fail(string error, string message)
        => new()
        {
            Success = false,
            Error = error,
            Message = message
        };
}
