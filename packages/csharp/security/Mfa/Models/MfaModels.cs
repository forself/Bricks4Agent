using System;
using System.Collections.Generic;

namespace Bricks4Agent.Security.Mfa.Models
{
    /// <summary>
    /// MFA method types
    /// </summary>
    public enum MfaMethod
    {
        /// <summary>
        /// TOTP - Time-based One-Time Password (Google Authenticator, etc.)
        /// </summary>
        Totp = 1,

        /// <summary>
        /// Email OTP - One-time code sent via email
        /// </summary>
        Email = 2,

        /// <summary>
        /// SMS OTP - One-time code sent via SMS
        /// </summary>
        Sms = 3,

        /// <summary>
        /// Recovery Code - Backup codes for account recovery
        /// </summary>
        RecoveryCode = 99
    }

    /// <summary>
    /// User MFA configuration stored in database
    /// </summary>
    public class UserMfaConfig
    {
        /// <summary>
        /// Primary key
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// User ID reference
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Whether MFA is enabled for this user
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Primary MFA method
        /// </summary>
        public MfaMethod PrimaryMethod { get; set; }

        /// <summary>
        /// TOTP secret (Base32 encoded, encrypted at rest)
        /// </summary>
        public string TotpSecret { get; set; }

        /// <summary>
        /// Whether TOTP is verified and active
        /// </summary>
        public bool TotpVerified { get; set; }

        /// <summary>
        /// Email for OTP delivery (may differ from login email)
        /// </summary>
        public string OtpEmail { get; set; }

        /// <summary>
        /// Phone number for SMS OTP
        /// </summary>
        public string OtpPhone { get; set; }

        /// <summary>
        /// Number of remaining recovery codes
        /// </summary>
        public int RecoveryCodesRemaining { get; set; }

        /// <summary>
        /// When MFA was first enabled
        /// </summary>
        public DateTime? EnabledAt { get; set; }

        /// <summary>
        /// Last successful MFA verification
        /// </summary>
        public DateTime? LastVerifiedAt { get; set; }

        /// <summary>
        /// Failed verification attempts (reset on success)
        /// </summary>
        public int FailedAttempts { get; set; }

        /// <summary>
        /// Lockout until this time if too many failed attempts
        /// </summary>
        public DateTime? LockedUntil { get; set; }

        /// <summary>
        /// Created timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last updated timestamp
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Recovery code stored in database (hashed)
    /// </summary>
    public class MfaRecoveryCode
    {
        /// <summary>
        /// Primary key
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// User ID reference
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Hashed recovery code
        /// </summary>
        public string CodeHash { get; set; }

        /// <summary>
        /// Whether this code has been used
        /// </summary>
        public bool IsUsed { get; set; }

        /// <summary>
        /// When this code was used
        /// </summary>
        public DateTime? UsedAt { get; set; }

        /// <summary>
        /// Created timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Email/SMS OTP stored in database (short-lived)
    /// </summary>
    public class MfaOtpCode
    {
        /// <summary>
        /// Primary key
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// User ID reference
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// OTP code (hashed)
        /// </summary>
        public string CodeHash { get; set; }

        /// <summary>
        /// Method used to send this OTP
        /// </summary>
        public MfaMethod Method { get; set; }

        /// <summary>
        /// Destination (email or phone)
        /// </summary>
        public string Destination { get; set; }

        /// <summary>
        /// When this OTP expires
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Whether this OTP has been used
        /// </summary>
        public bool IsUsed { get; set; }

        /// <summary>
        /// Created timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    #region Request/Response DTOs

    /// <summary>
    /// Request to enable MFA
    /// </summary>
    public class EnableMfaRequest
    {
        /// <summary>
        /// Preferred MFA method
        /// </summary>
        public MfaMethod Method { get; set; } = MfaMethod.Totp;

        /// <summary>
        /// Email for OTP delivery (if using Email method)
        /// </summary>
        public string OtpEmail { get; set; }

        /// <summary>
        /// Phone for SMS OTP (if using SMS method)
        /// </summary>
        public string OtpPhone { get; set; }
    }

    /// <summary>
    /// Response when initiating MFA setup
    /// </summary>
    public class MfaSetupResponse
    {
        /// <summary>
        /// Whether setup was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if failed
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// MFA method being set up
        /// </summary>
        public MfaMethod Method { get; set; }

        /// <summary>
        /// TOTP secret (only for TOTP method)
        /// </summary>
        public string TotpSecret { get; set; }

        /// <summary>
        /// QR code URI for authenticator apps (only for TOTP method)
        /// </summary>
        public string QrCodeUri { get; set; }

        /// <summary>
        /// Recovery codes (shown once during setup)
        /// </summary>
        public List<string> RecoveryCodes { get; set; }

        /// <summary>
        /// Message for user
        /// </summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// Request to verify MFA code
    /// </summary>
    public class VerifyMfaRequest
    {
        /// <summary>
        /// MFA code entered by user
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// MFA method used
        /// </summary>
        public MfaMethod Method { get; set; } = MfaMethod.Totp;

        /// <summary>
        /// Whether this is a recovery code
        /// </summary>
        public bool IsRecoveryCode { get; set; }
    }

    /// <summary>
    /// MFA verification result
    /// </summary>
    public class MfaVerificationResult
    {
        /// <summary>
        /// Whether verification succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if failed
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Whether user is locked out
        /// </summary>
        public bool IsLockedOut { get; set; }

        /// <summary>
        /// Lockout remaining time
        /// </summary>
        public TimeSpan? LockoutRemaining { get; set; }

        /// <summary>
        /// Remaining failed attempts before lockout
        /// </summary>
        public int? AttemptsRemaining { get; set; }

        /// <summary>
        /// If recovery code was used, remaining count
        /// </summary>
        public int? RecoveryCodesRemaining { get; set; }

        public static MfaVerificationResult SuccessResult() => new() { Success = true };

        public static MfaVerificationResult FailResult(string error, int? attemptsRemaining = null) =>
            new() { Success = false, Error = error, AttemptsRemaining = attemptsRemaining };

        public static MfaVerificationResult LockedResult(TimeSpan remaining) =>
            new() { Success = false, Error = "Account temporarily locked", IsLockedOut = true, LockoutRemaining = remaining };
    }

    /// <summary>
    /// Login result with MFA state
    /// </summary>
    public class MfaLoginResult
    {
        /// <summary>
        /// Whether login succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Whether MFA is required
        /// </summary>
        public bool RequiresMfa { get; set; }

        /// <summary>
        /// Temporary token for MFA verification step
        /// </summary>
        public string MfaToken { get; set; }

        /// <summary>
        /// Available MFA methods for this user
        /// </summary>
        public List<MfaMethod> AvailableMethods { get; set; }

        /// <summary>
        /// Final access token (only after MFA verified)
        /// </summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// Refresh token (only after MFA verified)
        /// </summary>
        public string RefreshToken { get; set; }

        /// <summary>
        /// User info (only after MFA verified)
        /// </summary>
        public UserInfo User { get; set; }

        /// <summary>
        /// Error message if failed
        /// </summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// Basic user info
    /// </summary>
    public class UserInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public bool MfaEnabled { get; set; }
    }

    /// <summary>
    /// Registration request with optional MFA setup
    /// </summary>
    public class RegisterWithMfaRequest
    {
        /// <summary>
        /// User's display name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// User's email (used for login)
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Password
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Whether to enable MFA during registration
        /// </summary>
        public bool EnableMfa { get; set; }

        /// <summary>
        /// Preferred MFA method if enabling
        /// </summary>
        public MfaMethod MfaMethod { get; set; } = MfaMethod.Totp;
    }

    /// <summary>
    /// Registration result
    /// </summary>
    public class RegisterWithMfaResult
    {
        /// <summary>
        /// Whether registration succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if failed
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// New user ID
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// MFA setup info (if MFA was enabled)
        /// </summary>
        public MfaSetupResponse MfaSetup { get; set; }

        /// <summary>
        /// Message for user
        /// </summary>
        public string Message { get; set; }
    }

    #endregion
}
