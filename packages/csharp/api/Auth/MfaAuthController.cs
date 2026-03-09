using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Bricks4Agent.Security.Mfa;
using Bricks4Agent.Security.Mfa.Models;

namespace Bricks4Agent.Api.Auth
{
    /// <summary>
    /// Authentication controller with MFA support
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public class MfaAuthController : ControllerBase
    {
        private readonly IMfaAuthService _authService;
        private readonly ILogger<MfaAuthController> _logger;

        public MfaAuthController(
            IMfaAuthService authService,
            ILogger<MfaAuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        /// <summary>
        /// Register a new user
        /// </summary>
        /// <param name="request">Registration request</param>
        /// <returns>Registration result with optional MFA setup info</returns>
        [HttpPost("register")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(RegisterWithMfaResult), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public IActionResult Register([FromBody] RegisterWithMfaRequest request)
        {
            if (request == null)
            {
                return BadRequest(new ApiError { Error = "Request body is required" });
            }

            var result = _authService.Register(request);

            if (!result.Success)
            {
                return BadRequest(new ApiError { Error = result.Error });
            }

            _logger.LogInformation("User registered: {UserId}, MFA enabled: {MfaEnabled}",
                result.UserId, request.EnableMfa);

            return Created($"/api/users/{result.UserId}", result);
        }

        /// <summary>
        /// Login (step 1) - verify credentials
        /// </summary>
        /// <param name="request">Login credentials</param>
        /// <returns>Login result or MFA challenge</returns>
        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(MfaLoginResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Unauthorized(new ApiError { Error = "Invalid credentials" });
            }

            var result = _authService.Login(request.Email, request.Password);

            if (!result.Success && !result.RequiresMfa)
            {
                // Don't log failed attempts with email to avoid log poisoning
                _logger.LogWarning("Failed login attempt");
                return Unauthorized(new ApiError { Error = result.Error ?? "Invalid credentials" });
            }

            if (result.RequiresMfa)
            {
                _logger.LogInformation("MFA required for login");
            }
            else
            {
                _logger.LogInformation("User logged in: {UserId}", result.User?.Id);
            }

            return Ok(result);
        }

        /// <summary>
        /// Login (step 2) - verify MFA code
        /// </summary>
        /// <param name="request">MFA verification request</param>
        /// <returns>Login result with tokens</returns>
        [HttpPost("login/mfa")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(MfaLoginResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
        public IActionResult VerifyMfaLogin([FromBody] MfaLoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.MfaToken) || string.IsNullOrWhiteSpace(request.Code))
            {
                return Unauthorized(new ApiError { Error = "Invalid request" });
            }

            var result = _authService.VerifyMfaLogin(
                request.MfaToken,
                request.Code,
                request.Method,
                request.IsRecoveryCode);

            if (!result.Success)
            {
                _logger.LogWarning("Failed MFA verification");
                return Unauthorized(new ApiError { Error = result.Error ?? "Invalid MFA code" });
            }

            _logger.LogInformation("User logged in with MFA: {UserId}", result.User?.Id);
            return Ok(result);
        }

        /// <summary>
        /// Request email OTP for MFA login
        /// </summary>
        /// <param name="request">Email OTP request</param>
        /// <returns>Success status</returns>
        [HttpPost("login/mfa/email")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public IActionResult RequestEmailOtp([FromBody] EmailOtpRequest request)
        {
            // Note: This endpoint should validate the MFA token before sending email
            // For security, always return success to prevent user enumeration

            if (request != null && !string.IsNullOrWhiteSpace(request.MfaToken))
            {
                // Validate MFA token and send email if valid
                // Implementation depends on how you want to extract userId from MFA token
            }

            return Ok(new { message = "If your account has email MFA configured, a code has been sent" });
        }

        /// <summary>
        /// Get current user's MFA status
        /// </summary>
        /// <returns>MFA status</returns>
        [HttpGet("mfa/status")]
        [Authorize]
        [ProducesResponseType(typeof(MfaStatusResponse), StatusCodes.Status200OK)]
        public IActionResult GetMfaStatus()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var status = _authService.GetMfaStatus(userId.Value);
            return Ok(status);
        }

        /// <summary>
        /// Enable MFA for current user
        /// </summary>
        /// <param name="request">MFA setup request</param>
        /// <returns>Setup response with QR code / secret</returns>
        [HttpPost("mfa/enable")]
        [Authorize]
        [ProducesResponseType(typeof(MfaSetupResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public IActionResult EnableMfa([FromBody] EnableMfaRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            request ??= new EnableMfaRequest { Method = MfaMethod.Totp };

            var result = _authService.EnableMfa(userId.Value, request);

            if (!result.Success)
            {
                return BadRequest(new ApiError { Error = result.Error });
            }

            _logger.LogInformation("MFA setup initiated for user: {UserId}, method: {Method}",
                userId.Value, request.Method);

            return Ok(result);
        }

        /// <summary>
        /// Verify MFA setup
        /// </summary>
        /// <param name="request">Verification code</param>
        /// <returns>Verification result with recovery codes</returns>
        [HttpPost("mfa/verify")]
        [Authorize]
        [ProducesResponseType(typeof(MfaVerifyResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public IActionResult VerifyMfaSetup([FromBody] VerifyMfaRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new ApiError { Error = "Code is required" });
            }

            var result = _authService.VerifyMfaSetup(userId.Value, request.Code, request.Method);

            if (!result.Success)
            {
                return BadRequest(new ApiError { Error = result.Error });
            }

            // Get fresh recovery codes after setup
            var recoveryCodes = _authService.RegenerateRecoveryCodes(userId.Value, request.Code);

            _logger.LogInformation("MFA enabled for user: {UserId}", userId.Value);

            return Ok(new MfaVerifyResponse
            {
                Success = true,
                RecoveryCodes = recoveryCodes,
                Message = "MFA enabled successfully. Please save your recovery codes in a safe place."
            });
        }

        /// <summary>
        /// Disable MFA for current user
        /// </summary>
        /// <param name="request">Current MFA code for verification</param>
        /// <returns>Success status</returns>
        [HttpPost("mfa/disable")]
        [Authorize]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public IActionResult DisableMfa([FromBody] DisableMfaRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new ApiError { Error = "Verification code is required" });
            }

            var result = _authService.DisableMfa(userId.Value, request.Code);

            if (!result)
            {
                return BadRequest(new ApiError { Error = "Failed to disable MFA. Invalid verification code." });
            }

            _logger.LogInformation("MFA disabled for user: {UserId}", userId.Value);

            return Ok(new { message = "MFA disabled successfully" });
        }

        /// <summary>
        /// Regenerate recovery codes
        /// </summary>
        /// <param name="request">Current MFA code for verification</param>
        /// <returns>New recovery codes</returns>
        [HttpPost("mfa/recovery-codes")]
        [Authorize]
        [ProducesResponseType(typeof(RecoveryCodesResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public IActionResult RegenerateRecoveryCodes([FromBody] DisableMfaRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new ApiError { Error = "Verification code is required" });
            }

            var codes = _authService.RegenerateRecoveryCodes(userId.Value, request.Code);

            if (codes == null)
            {
                return BadRequest(new ApiError { Error = "Failed to regenerate recovery codes. Invalid verification code." });
            }

            _logger.LogInformation("Recovery codes regenerated for user: {UserId}", userId.Value);

            return Ok(new RecoveryCodesResponse
            {
                RecoveryCodes = codes,
                Message = "New recovery codes generated. Please save them in a safe place. Your old codes are now invalid."
            });
        }

        #region Helper Methods

        private int? GetCurrentUserId()
        {
            var userIdClaim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? User?.FindFirst("sub")?.Value;

            if (int.TryParse(userIdClaim, out int userId))
            {
                return userId;
            }
            return null;
        }

        #endregion
    }

    #region Request/Response DTOs

    /// <summary>
    /// Login request
    /// </summary>
    public class LoginRequest
    {
        /// <summary>
        /// User email
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// User password
        /// </summary>
        public string Password { get; set; }
    }

    /// <summary>
    /// MFA login request (step 2)
    /// </summary>
    public class MfaLoginRequest
    {
        /// <summary>
        /// MFA token from step 1
        /// </summary>
        public string MfaToken { get; set; }

        /// <summary>
        /// MFA code
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
    /// Email OTP request
    /// </summary>
    public class EmailOtpRequest
    {
        /// <summary>
        /// MFA token from login step 1
        /// </summary>
        public string MfaToken { get; set; }
    }

    /// <summary>
    /// Disable MFA request
    /// </summary>
    public class DisableMfaRequest
    {
        /// <summary>
        /// Current MFA code for verification
        /// </summary>
        public string Code { get; set; }
    }

    /// <summary>
    /// MFA verify response
    /// </summary>
    public class MfaVerifyResponse
    {
        public bool Success { get; set; }
        public List<string> RecoveryCodes { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Recovery codes response
    /// </summary>
    public class RecoveryCodesResponse
    {
        public List<string> RecoveryCodes { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// API error response
    /// </summary>
    public class ApiError
    {
        public string Error { get; set; }
    }

    #endregion
}
