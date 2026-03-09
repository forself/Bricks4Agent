using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Bricks4Agent.Security.Mfa.Models;

namespace Bricks4Agent.Security.Mfa
{
    /// <summary>
    /// Authentication service with MFA support
    /// </summary>
    public interface IMfaAuthService
    {
        /// <summary>
        /// Register a new user with optional MFA setup
        /// </summary>
        RegisterWithMfaResult Register(RegisterWithMfaRequest request);

        /// <summary>
        /// Login (step 1) - verify credentials
        /// </summary>
        MfaLoginResult Login(string email, string password);

        /// <summary>
        /// Login (step 2) - verify MFA code
        /// </summary>
        MfaLoginResult VerifyMfaLogin(string mfaToken, string code, MfaMethod method, bool isRecoveryCode = false);

        /// <summary>
        /// Enable MFA for current user
        /// </summary>
        MfaSetupResponse EnableMfa(int userId, EnableMfaRequest request);

        /// <summary>
        /// Verify MFA setup
        /// </summary>
        MfaVerificationResult VerifyMfaSetup(int userId, string code, MfaMethod method);

        /// <summary>
        /// Disable MFA for current user
        /// </summary>
        bool DisableMfa(int userId, string verificationCode);

        /// <summary>
        /// Get user's MFA status
        /// </summary>
        MfaStatusResponse GetMfaStatus(int userId);

        /// <summary>
        /// Regenerate recovery codes
        /// </summary>
        List<string> RegenerateRecoveryCodes(int userId, string verificationCode);

        /// <summary>
        /// Request email OTP for MFA
        /// </summary>
        bool SendMfaEmailOtp(int userId);
    }

    /// <summary>
    /// MFA-enabled authentication service implementation
    /// </summary>
    public class MfaAuthService : IMfaAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IMfaService _mfaService;
        private readonly IConfiguration _configuration;
        private readonly string _jwtKey;
        private readonly string _jwtIssuer;
        private readonly int _jwtExpirationMinutes;
        private readonly int _mfaTokenExpirationMinutes;

        // Password hashing settings (PBKDF2)
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100000;

        public MfaAuthService(
            IUserRepository userRepository,
            IMfaService mfaService,
            IConfiguration configuration)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _mfaService = mfaService ?? throw new ArgumentNullException(nameof(mfaService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            _jwtKey = configuration["Jwt:Key"] ?? throw new ArgumentException("Jwt:Key is required");
            _jwtIssuer = configuration["Jwt:Issuer"] ?? "YourApp";

            if (!int.TryParse(configuration["Jwt:ExpirationMinutes"], out _jwtExpirationMinutes) || _jwtExpirationMinutes <= 0)
                _jwtExpirationMinutes = 60;

            if (!int.TryParse(configuration["Mfa:TokenExpirationMinutes"], out _mfaTokenExpirationMinutes) || _mfaTokenExpirationMinutes <= 0)
                _mfaTokenExpirationMinutes = 5;
        }

        /// <inheritdoc />
        public RegisterWithMfaResult Register(RegisterWithMfaRequest request)
        {
            // Validate request
            var validation = ValidateRegistration(request);
            if (!validation.IsValid)
            {
                return new RegisterWithMfaResult { Success = false, Error = validation.Error };
            }

            // Check if email exists
            if (_userRepository.EmailExists(request.Email))
            {
                return new RegisterWithMfaResult { Success = false, Error = "Email already registered" };
            }

            // Create user
            var passwordHash = HashPassword(request.Password);
            var userId = _userRepository.CreateUser(new UserCreateModel
            {
                Name = request.Name.Trim(),
                Email = request.Email.Trim().ToLowerInvariant(),
                PasswordHash = passwordHash,
                Role = "user",
                Status = "active"
            });

            var result = new RegisterWithMfaResult
            {
                Success = true,
                UserId = userId,
                Message = "Registration successful"
            };

            // Setup MFA if requested
            if (request.EnableMfa)
            {
                var mfaSetup = _mfaService.InitiateSetup(userId, request.Email, request.MfaMethod);
                result.MfaSetup = mfaSetup;
                result.Message = "Registration successful. Please complete MFA setup.";
            }

            return result;
        }

        /// <inheritdoc />
        public MfaLoginResult Login(string email, string password)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return new MfaLoginResult { Success = false, Error = "Invalid credentials" };
            }

            email = email.Trim().ToLowerInvariant();

            // Get user
            var user = _userRepository.GetUserByEmail(email);
            if (user == null)
            {
                // Don't reveal if user exists
                return new MfaLoginResult { Success = false, Error = "Invalid credentials" };
            }

            // Verify password
            if (!VerifyPassword(password, user.PasswordHash))
            {
                return new MfaLoginResult { Success = false, Error = "Invalid credentials" };
            }

            // Check if user is active
            if (user.Status != "active")
            {
                return new MfaLoginResult { Success = false, Error = "Account is not active" };
            }

            // Check if MFA is required
            if (_mfaService.IsMfaRequired(user.Id))
            {
                var mfaConfig = _mfaService.GetMfaStatus(user.Id);
                var availableMethods = GetAvailableMfaMethods(mfaConfig);

                // Generate temporary MFA token
                var mfaToken = GenerateMfaToken(user.Id);

                return new MfaLoginResult
                {
                    Success = true,
                    RequiresMfa = true,
                    MfaToken = mfaToken,
                    AvailableMethods = availableMethods
                };
            }

            // No MFA - complete login
            return CompleteLogin(user);
        }

        /// <inheritdoc />
        public MfaLoginResult VerifyMfaLogin(string mfaToken, string code, MfaMethod method, bool isRecoveryCode = false)
        {
            // Validate MFA token
            var userId = ValidateMfaToken(mfaToken);
            if (userId == null)
            {
                return new MfaLoginResult { Success = false, Error = "Invalid or expired MFA session" };
            }

            // Verify MFA code
            MfaVerificationResult result;
            if (isRecoveryCode)
            {
                result = _mfaService.VerifyRecoveryCode(userId.Value, code);
            }
            else
            {
                result = _mfaService.VerifyCode(userId.Value, code, method);
            }

            if (!result.Success)
            {
                return new MfaLoginResult
                {
                    Success = false,
                    Error = result.Error ?? "Invalid MFA code"
                };
            }

            // Get user and complete login
            var user = _userRepository.GetUserById(userId.Value);
            if (user == null)
            {
                return new MfaLoginResult { Success = false, Error = "User not found" };
            }

            var loginResult = CompleteLogin(user);

            // Add recovery code warning if used
            if (isRecoveryCode && result.RecoveryCodesRemaining.HasValue)
            {
                loginResult.Error = $"Recovery code used. {result.RecoveryCodesRemaining} codes remaining.";
            }

            return loginResult;
        }

        /// <inheritdoc />
        public MfaSetupResponse EnableMfa(int userId, EnableMfaRequest request)
        {
            var user = _userRepository.GetUserById(userId);
            if (user == null)
            {
                return new MfaSetupResponse { Success = false, Error = "User not found" };
            }

            return _mfaService.InitiateSetup(userId, user.Email, request.Method);
        }

        /// <inheritdoc />
        public MfaVerificationResult VerifyMfaSetup(int userId, string code, MfaMethod method)
        {
            var result = _mfaService.VerifySetup(userId, code, method);

            if (result.Success)
            {
                // Generate recovery codes and return them
                var recoveryCodes = _mfaService.GenerateRecoveryCodes(userId);
                result.RecoveryCodesRemaining = recoveryCodes.Count;
            }

            return result;
        }

        /// <inheritdoc />
        public bool DisableMfa(int userId, string verificationCode)
        {
            return _mfaService.DisableMfa(userId, verificationCode);
        }

        /// <inheritdoc />
        public MfaStatusResponse GetMfaStatus(int userId)
        {
            var config = _mfaService.GetMfaStatus(userId);
            if (config == null)
            {
                return new MfaStatusResponse
                {
                    MfaEnabled = false,
                    AvailableMethods = new List<MfaMethod>()
                };
            }

            return new MfaStatusResponse
            {
                MfaEnabled = config.IsEnabled,
                PrimaryMethod = config.PrimaryMethod,
                TotpConfigured = config.TotpVerified,
                EmailConfigured = !string.IsNullOrEmpty(config.OtpEmail),
                RecoveryCodesRemaining = config.RecoveryCodesRemaining,
                EnabledAt = config.EnabledAt,
                LastVerifiedAt = config.LastVerifiedAt,
                AvailableMethods = GetAvailableMfaMethods(config)
            };
        }

        /// <inheritdoc />
        public List<string> RegenerateRecoveryCodes(int userId, string verificationCode)
        {
            // Verify current code first
            var config = _mfaService.GetMfaStatus(userId);
            if (config == null || !config.IsEnabled)
            {
                return null;
            }

            var result = _mfaService.VerifyCode(userId, verificationCode, config.PrimaryMethod);
            if (!result.Success)
            {
                return null;
            }

            return _mfaService.GenerateRecoveryCodes(userId);
        }

        /// <inheritdoc />
        public bool SendMfaEmailOtp(int userId)
        {
            var config = _mfaService.GetMfaStatus(userId);
            if (config == null || string.IsNullOrEmpty(config.OtpEmail))
            {
                return false;
            }

            return _mfaService.SendEmailOtp(userId, config.OtpEmail);
        }

        #region Private Methods

        private MfaLoginResult CompleteLogin(UserModel user)
        {
            // Generate tokens
            var accessToken = GenerateJwtToken(user);
            var refreshToken = GenerateRefreshToken();

            // Update last login
            _userRepository.UpdateLastLogin(user.Id);

            var mfaConfig = _mfaService.GetMfaStatus(user.Id);

            return new MfaLoginResult
            {
                Success = true,
                RequiresMfa = false,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                User = new UserInfo
                {
                    Id = user.Id,
                    Name = user.Name,
                    Email = user.Email,
                    Role = user.Role,
                    MfaEnabled = mfaConfig?.IsEnabled ?? false
                }
            };
        }

        private string GenerateJwtToken(UserModel user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("mfa_verified", "true")
            };

            var token = new JwtSecurityToken(
                issuer: _jwtIssuer,
                audience: _jwtIssuer,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_jwtExpirationMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GenerateMfaToken(int userId)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim("mfa_user_id", userId.ToString()),
                new Claim("mfa_pending", "true"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _jwtIssuer,
                audience: _jwtIssuer,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_mfaTokenExpirationMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private int? ValidateMfaToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_jwtKey);

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _jwtIssuer,
                    ValidAudience = _jwtIssuer,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.Zero
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

                // Check if it's an MFA pending token
                var mfaPending = principal.FindFirst("mfa_pending")?.Value;
                if (mfaPending != "true")
                    return null;

                var userIdClaim = principal.FindFirst("mfa_user_id")?.Value;
                if (int.TryParse(userIdClaim, out int userId))
                    return userId;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string GenerateRefreshToken()
        {
            var bytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static List<MfaMethod> GetAvailableMfaMethods(UserMfaConfig config)
        {
            var methods = new List<MfaMethod>();

            if (config == null)
                return methods;

            if (config.TotpVerified)
                methods.Add(MfaMethod.Totp);

            if (!string.IsNullOrEmpty(config.OtpEmail))
                methods.Add(MfaMethod.Email);

            if (!string.IsNullOrEmpty(config.OtpPhone))
                methods.Add(MfaMethod.Sms);

            if (config.RecoveryCodesRemaining > 0)
                methods.Add(MfaMethod.RecoveryCode);

            return methods;
        }

        private (bool IsValid, string Error) ValidateRegistration(RegisterWithMfaRequest request)
        {
            if (request == null)
                return (false, "Request is required");

            if (string.IsNullOrWhiteSpace(request.Name))
                return (false, "Name is required");

            if (request.Name.Length > 100)
                return (false, "Name cannot exceed 100 characters");

            if (string.IsNullOrWhiteSpace(request.Email))
                return (false, "Email is required");

            if (!IsValidEmail(request.Email))
                return (false, "Invalid email format");

            if (string.IsNullOrWhiteSpace(request.Password))
                return (false, "Password is required");

            if (request.Password.Length < 8)
                return (false, "Password must be at least 8 characters");

            if (request.Password.Length > 128)
                return (false, "Password cannot exceed 128 characters");

            return (true, null);
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
                return false;

            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(
                    email.Trim(),
                    @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(250));
            }
            catch
            {
                return false;
            }
        }

        #region Password Hashing (PBKDF2)

        private static string HashPassword(string password)
        {
            var salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256);

            var hash = pbkdf2.GetBytes(HashSize);

            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        private static bool VerifyPassword(string password, string storedHash)
        {
            try
            {
                var parts = storedHash.Split('.');
                if (parts.Length != 3)
                    return false;

                var iterations = int.Parse(parts[0]);
                var salt = Convert.FromBase64String(parts[1]);
                var hash = Convert.FromBase64String(parts[2]);

                using var pbkdf2 = new Rfc2898DeriveBytes(
                    password,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256);

                var computedHash = pbkdf2.GetBytes(hash.Length);

                return CryptographicEquals(hash, computedHash);
            }
            catch
            {
                return false;
            }
        }

        private static bool CryptographicEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            var result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }

        #endregion

        #endregion
    }

    /// <summary>
    /// MFA status response
    /// </summary>
    public class MfaStatusResponse
    {
        public bool MfaEnabled { get; set; }
        public MfaMethod PrimaryMethod { get; set; }
        public bool TotpConfigured { get; set; }
        public bool EmailConfigured { get; set; }
        public bool SmsConfigured { get; set; }
        public int RecoveryCodesRemaining { get; set; }
        public DateTime? EnabledAt { get; set; }
        public DateTime? LastVerifiedAt { get; set; }
        public List<MfaMethod> AvailableMethods { get; set; }
    }

    /// <summary>
    /// User repository interface for MFA auth service
    /// </summary>
    public interface IUserRepository
    {
        UserModel GetUserById(int id);
        UserModel GetUserByEmail(string email);
        bool EmailExists(string email);
        int CreateUser(UserCreateModel model);
        void UpdateLastLogin(int userId);
    }

    /// <summary>
    /// User model for authentication
    /// </summary>
    public class UserModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; }
        public string Status { get; set; }
    }

    /// <summary>
    /// User creation model
    /// </summary>
    public class UserCreateModel
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; }
        public string Status { get; set; }
    }
}
