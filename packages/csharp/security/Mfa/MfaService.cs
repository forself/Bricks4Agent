using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Bricks4Agent.Security.Mfa.Models;

namespace Bricks4Agent.Security.Mfa
{
    /// <summary>
    /// MFA service interface
    /// </summary>
    public interface IMfaService
    {
        /// <summary>
        /// Initiate MFA setup for a user
        /// </summary>
        MfaSetupResponse InitiateSetup(int userId, string email, MfaMethod method);

        /// <summary>
        /// Verify and complete MFA setup
        /// </summary>
        MfaVerificationResult VerifySetup(int userId, string code, MfaMethod method);

        /// <summary>
        /// Verify MFA code during login
        /// </summary>
        MfaVerificationResult VerifyCode(int userId, string code, MfaMethod method);

        /// <summary>
        /// Verify recovery code
        /// </summary>
        MfaVerificationResult VerifyRecoveryCode(int userId, string code);

        /// <summary>
        /// Generate new recovery codes
        /// </summary>
        List<string> GenerateRecoveryCodes(int userId);

        /// <summary>
        /// Send OTP via email
        /// </summary>
        bool SendEmailOtp(int userId, string email);

        /// <summary>
        /// Disable MFA for a user
        /// </summary>
        bool DisableMfa(int userId, string verificationCode);

        /// <summary>
        /// Get MFA status for a user
        /// </summary>
        UserMfaConfig GetMfaStatus(int userId);

        /// <summary>
        /// Check if MFA is required for user
        /// </summary>
        bool IsMfaRequired(int userId);
    }

    /// <summary>
    /// MFA service implementation
    /// Note: This implementation uses an in-memory store for demonstration.
    /// In production, use a proper database repository.
    /// </summary>
    public class MfaService : IMfaService
    {
        private readonly IMfaRepository _repository;
        private readonly IEmailService _emailService;
        private readonly MfaOptions _options;
        private readonly string _appName;

        // Lockout settings
        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
        private const int RecoveryCodeCount = 10;
        private const int OtpCodeLength = 6;
        private static readonly TimeSpan OtpExpiration = TimeSpan.FromMinutes(10);

        public MfaService(
            IMfaRepository repository,
            IEmailService emailService = null,
            MfaOptions options = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _emailService = emailService;
            _options = options ?? new MfaOptions();
            _appName = _options.AppName ?? "YourApp";
        }

        /// <inheritdoc />
        public MfaSetupResponse InitiateSetup(int userId, string email, MfaMethod method)
        {
            if (userId <= 0)
                return new MfaSetupResponse { Success = false, Error = "Invalid user ID" };

            try
            {
                var config = _repository.GetUserMfaConfig(userId) ?? new UserMfaConfig { UserId = userId };

                switch (method)
                {
                    case MfaMethod.Totp:
                        return InitiateTotpSetup(config, email);

                    case MfaMethod.Email:
                        return InitiateEmailSetup(config, email);

                    default:
                        return new MfaSetupResponse { Success = false, Error = "Unsupported MFA method" };
                }
            }
            catch (Exception ex)
            {
                return new MfaSetupResponse { Success = false, Error = "Failed to initiate MFA setup" };
            }
        }

        private MfaSetupResponse InitiateTotpSetup(UserMfaConfig config, string email)
        {
            // Generate new TOTP secret
            var secret = TotpGenerator.GenerateSecret();
            config.TotpSecret = secret;
            config.TotpVerified = false;
            config.PrimaryMethod = MfaMethod.Totp;
            config.UpdatedAt = DateTime.UtcNow;

            _repository.SaveUserMfaConfig(config);

            // Generate QR code URI
            var qrUri = TotpGenerator.GenerateOtpAuthUri(secret, email, _appName);

            return new MfaSetupResponse
            {
                Success = true,
                Method = MfaMethod.Totp,
                TotpSecret = secret,
                QrCodeUri = qrUri,
                Message = "Scan the QR code with your authenticator app, then enter the code to verify"
            };
        }

        private MfaSetupResponse InitiateEmailSetup(UserMfaConfig config, string email)
        {
            if (string.IsNullOrEmpty(email))
                return new MfaSetupResponse { Success = false, Error = "Email is required for email-based MFA" };

            config.OtpEmail = email;
            config.PrimaryMethod = MfaMethod.Email;
            config.UpdatedAt = DateTime.UtcNow;

            _repository.SaveUserMfaConfig(config);

            // Send verification OTP
            SendEmailOtp(config.UserId, email);

            return new MfaSetupResponse
            {
                Success = true,
                Method = MfaMethod.Email,
                Message = $"Verification code sent to {MaskEmail(email)}"
            };
        }

        /// <inheritdoc />
        public MfaVerificationResult VerifySetup(int userId, string code, MfaMethod method)
        {
            var config = _repository.GetUserMfaConfig(userId);
            if (config == null)
                return MfaVerificationResult.FailResult("MFA not configured");

            // Check lockout
            var lockoutResult = CheckLockout(config);
            if (lockoutResult != null)
                return lockoutResult;

            bool isValid = method switch
            {
                MfaMethod.Totp => TotpGenerator.ValidateCode(config.TotpSecret, code),
                MfaMethod.Email => VerifyEmailOtp(userId, code),
                _ => false
            };

            if (!isValid)
            {
                RecordFailedAttempt(config);
                var remaining = MaxFailedAttempts - config.FailedAttempts;
                return MfaVerificationResult.FailResult("Invalid verification code", remaining);
            }

            // Setup complete
            config.IsEnabled = true;
            config.TotpVerified = method == MfaMethod.Totp;
            config.EnabledAt = DateTime.UtcNow;
            config.LastVerifiedAt = DateTime.UtcNow;
            config.FailedAttempts = 0;
            config.UpdatedAt = DateTime.UtcNow;

            // Generate recovery codes
            var recoveryCodes = GenerateRecoveryCodesInternal(config);
            config.RecoveryCodesRemaining = recoveryCodes.Count;

            _repository.SaveUserMfaConfig(config);

            return new MfaVerificationResult
            {
                Success = true,
                RecoveryCodesRemaining = recoveryCodes.Count
            };
        }

        /// <inheritdoc />
        public MfaVerificationResult VerifyCode(int userId, string code, MfaMethod method)
        {
            var config = _repository.GetUserMfaConfig(userId);
            if (config == null || !config.IsEnabled)
                return MfaVerificationResult.FailResult("MFA not enabled");

            // Check lockout
            var lockoutResult = CheckLockout(config);
            if (lockoutResult != null)
                return lockoutResult;

            bool isValid = method switch
            {
                MfaMethod.Totp => TotpGenerator.ValidateCode(config.TotpSecret, code),
                MfaMethod.Email => VerifyEmailOtp(userId, code),
                _ => false
            };

            if (!isValid)
            {
                RecordFailedAttempt(config);
                var remaining = MaxFailedAttempts - config.FailedAttempts;
                return MfaVerificationResult.FailResult("Invalid code", remaining);
            }

            // Success
            config.LastVerifiedAt = DateTime.UtcNow;
            config.FailedAttempts = 0;
            config.UpdatedAt = DateTime.UtcNow;
            _repository.SaveUserMfaConfig(config);

            return MfaVerificationResult.SuccessResult();
        }

        /// <inheritdoc />
        public MfaVerificationResult VerifyRecoveryCode(int userId, string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return MfaVerificationResult.FailResult("Recovery code is required");

            var config = _repository.GetUserMfaConfig(userId);
            if (config == null)
                return MfaVerificationResult.FailResult("MFA not configured");

            // Check lockout
            var lockoutResult = CheckLockout(config);
            if (lockoutResult != null)
                return lockoutResult;

            // Normalize code
            code = code.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
            var codeHash = HashCode(code);

            // Find and validate recovery code
            var recoveryCode = _repository.GetRecoveryCode(userId, codeHash);
            if (recoveryCode == null || recoveryCode.IsUsed)
            {
                RecordFailedAttempt(config);
                return MfaVerificationResult.FailResult("Invalid recovery code");
            }

            // Mark code as used
            recoveryCode.IsUsed = true;
            recoveryCode.UsedAt = DateTime.UtcNow;
            _repository.MarkRecoveryCodeUsed(recoveryCode);

            // Update config
            config.RecoveryCodesRemaining--;
            config.LastVerifiedAt = DateTime.UtcNow;
            config.FailedAttempts = 0;
            config.UpdatedAt = DateTime.UtcNow;
            _repository.SaveUserMfaConfig(config);

            return new MfaVerificationResult
            {
                Success = true,
                RecoveryCodesRemaining = config.RecoveryCodesRemaining
            };
        }

        /// <inheritdoc />
        public List<string> GenerateRecoveryCodes(int userId)
        {
            var config = _repository.GetUserMfaConfig(userId);
            if (config == null)
                return new List<string>();

            var codes = GenerateRecoveryCodesInternal(config);
            config.RecoveryCodesRemaining = codes.Count;
            config.UpdatedAt = DateTime.UtcNow;
            _repository.SaveUserMfaConfig(config);

            return codes;
        }

        private List<string> GenerateRecoveryCodesInternal(UserMfaConfig config)
        {
            // Delete existing recovery codes
            _repository.DeleteRecoveryCodes(config.UserId);

            var codes = new List<string>();
            for (int i = 0; i < RecoveryCodeCount; i++)
            {
                var code = GenerateRecoveryCodeString();
                codes.Add(code);

                var codeHash = HashCode(code.Replace("-", ""));
                var recoveryCode = new MfaRecoveryCode
                {
                    UserId = config.UserId,
                    CodeHash = codeHash,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                };
                _repository.SaveRecoveryCode(recoveryCode);
            }

            return codes;
        }

        /// <inheritdoc />
        public bool SendEmailOtp(int userId, string email)
        {
            if (_emailService == null)
                return false;

            if (string.IsNullOrEmpty(email))
                return false;

            // Generate OTP
            var otp = GenerateNumericOtp(OtpCodeLength);
            var otpHash = HashCode(otp);

            // Save OTP
            var otpCode = new MfaOtpCode
            {
                UserId = userId,
                CodeHash = otpHash,
                Method = MfaMethod.Email,
                Destination = email,
                ExpiresAt = DateTime.UtcNow.Add(OtpExpiration),
                CreatedAt = DateTime.UtcNow
            };
            _repository.SaveOtpCode(otpCode);

            // Send email
            var subject = $"{_appName} - Your verification code";
            var body = $"Your verification code is: {otp}\n\nThis code will expire in {OtpExpiration.TotalMinutes} minutes.\n\nIf you did not request this code, please ignore this email.";

            return _emailService.SendEmail(email, subject, body);
        }

        private bool VerifyEmailOtp(int userId, string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return false;

            code = code.Trim();
            var codeHash = HashCode(code);

            var otpCode = _repository.GetValidOtpCode(userId, codeHash, MfaMethod.Email);
            if (otpCode == null)
                return false;

            // Mark as used
            otpCode.IsUsed = true;
            _repository.MarkOtpCodeUsed(otpCode);

            return true;
        }

        /// <inheritdoc />
        public bool DisableMfa(int userId, string verificationCode)
        {
            var config = _repository.GetUserMfaConfig(userId);
            if (config == null || !config.IsEnabled)
                return false;

            // Verify code before disabling
            var result = VerifyCode(userId, verificationCode, config.PrimaryMethod);
            if (!result.Success)
                return false;

            // Disable MFA
            config.IsEnabled = false;
            config.TotpSecret = null;
            config.TotpVerified = false;
            config.UpdatedAt = DateTime.UtcNow;

            _repository.SaveUserMfaConfig(config);
            _repository.DeleteRecoveryCodes(userId);

            return true;
        }

        /// <inheritdoc />
        public UserMfaConfig GetMfaStatus(int userId)
        {
            return _repository.GetUserMfaConfig(userId);
        }

        /// <inheritdoc />
        public bool IsMfaRequired(int userId)
        {
            var config = _repository.GetUserMfaConfig(userId);
            return config?.IsEnabled ?? false;
        }

        #region Private Helpers

        private MfaVerificationResult CheckLockout(UserMfaConfig config)
        {
            if (config.LockedUntil.HasValue && config.LockedUntil > DateTime.UtcNow)
            {
                var remaining = config.LockedUntil.Value - DateTime.UtcNow;
                return MfaVerificationResult.LockedResult(remaining);
            }
            return null;
        }

        private void RecordFailedAttempt(UserMfaConfig config)
        {
            config.FailedAttempts++;
            if (config.FailedAttempts >= MaxFailedAttempts)
            {
                config.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                config.FailedAttempts = 0;
            }
            config.UpdatedAt = DateTime.UtcNow;
            _repository.SaveUserMfaConfig(config);
        }

        private static string GenerateRecoveryCodeString()
        {
            // Format: XXXX-XXXX-XXXX (12 alphanumeric chars)
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // No I,O,0,1 for readability
            var code = new char[12];
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[12];
            rng.GetBytes(bytes);

            for (int i = 0; i < 12; i++)
            {
                code[i] = chars[bytes[i] % chars.Length];
            }

            return $"{new string(code, 0, 4)}-{new string(code, 4, 4)}-{new string(code, 8, 4)}";
        }

        private static string GenerateNumericOtp(int length)
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);

            var otp = new StringBuilder(length);
            foreach (var b in bytes)
            {
                otp.Append(b % 10);
            }
            return otp.ToString();
        }

        private static string HashCode(string code)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(code);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
                return email;

            var parts = email.Split('@');
            var local = parts[0];
            var domain = parts[1];

            if (local.Length <= 2)
                return $"{local[0]}***@{domain}";

            return $"{local[0]}***{local[local.Length - 1]}@{domain}";
        }

        #endregion
    }

    /// <summary>
    /// MFA configuration options
    /// </summary>
    public class MfaOptions
    {
        /// <summary>
        /// Application name shown in authenticator apps
        /// </summary>
        public string AppName { get; set; } = "YourApp";

        /// <summary>
        /// Whether to enforce MFA for all users
        /// </summary>
        public bool EnforceMfa { get; set; } = false;

        /// <summary>
        /// Roles that require MFA
        /// </summary>
        public List<string> MfaRequiredRoles { get; set; } = new() { "admin" };
    }

    /// <summary>
    /// MFA repository interface for data persistence
    /// </summary>
    public interface IMfaRepository
    {
        UserMfaConfig GetUserMfaConfig(int userId);
        void SaveUserMfaConfig(UserMfaConfig config);
        void DeleteRecoveryCodes(int userId);
        void SaveRecoveryCode(MfaRecoveryCode code);
        MfaRecoveryCode GetRecoveryCode(int userId, string codeHash);
        void MarkRecoveryCodeUsed(MfaRecoveryCode code);
        void SaveOtpCode(MfaOtpCode code);
        MfaOtpCode GetValidOtpCode(int userId, string codeHash, MfaMethod method);
        void MarkOtpCodeUsed(MfaOtpCode code);
    }

    /// <summary>
    /// Email service interface for sending OTP
    /// </summary>
    public interface IEmailService
    {
        bool SendEmail(string to, string subject, string body);
    }
}
