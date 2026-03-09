using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Bricks4Agent.Security.Mfa;
using Bricks4Agent.Security.Mfa.Models;
using Bricks4Agent.Security.RateLimiting;

namespace Bricks4Agent.Api.Auth
{
    /// <summary>
    /// Authentication controller with MFA and IP-based rate limiting
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public class RateLimitedAuthController : ControllerBase
    {
        private readonly IMfaAuthService _authService;
        private readonly IIpRateLimiter _rateLimiter;
        private readonly IConnectionInfoService _connectionInfo;
        private readonly IUserSessionService _sessionService;
        private readonly ILogger<RateLimitedAuthController> _logger;

        public RateLimitedAuthController(
            IMfaAuthService authService,
            IIpRateLimiter rateLimiter,
            IConnectionInfoService connectionInfo,
            IUserSessionService sessionService,
            ILogger<RateLimitedAuthController> logger)
        {
            _authService = authService;
            _rateLimiter = rateLimiter;
            _connectionInfo = connectionInfo;
            _sessionService = sessionService;
            _logger = logger;
        }

        /// <summary>
        /// Register a new user
        /// </summary>
        [HttpPost("register")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(RegisterWithMfaResult), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(RateLimitedError), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(RateLimitedError), StatusCodes.Status429TooManyRequests)]
        public IActionResult Register([FromBody] RegisterWithMfaRequest request)
        {
            var connInfo = _connectionInfo.GetConnectionInfo(HttpContext);
            var clientIp = connInfo.IpAddress;

            // Check rate limit
            var rateResult = _rateLimiter.CheckAndIncrement(clientIp, "register");
            AddRateLimitHeaders(rateResult);

            if (!rateResult.IsAllowed)
            {
                _logger.LogWarning("Registration rate limited for IP: {Ip}", MaskIp(clientIp));
                return TooManyRequests(rateResult, "Too many registration attempts");
            }

            if (request == null)
            {
                return BadRequest(new RateLimitedError { Error = "Request body is required" });
            }

            // Record attempt
            _sessionService.RecordLoginAttempt(new LoginAttempt
            {
                Identifier = request.Email,
                IpAddress = clientIp,
                UserAgent = connInfo.UserAgent,
                Success = false, // Will update if successful
                FailureReason = "Pending"
            });

            var result = _authService.Register(request);

            if (!result.Success)
            {
                return BadRequest(new RateLimitedError { Error = result.Error });
            }

            // Update attempt as successful
            _sessionService.RecordLoginAttempt(new LoginAttempt
            {
                Identifier = request.Email,
                IpAddress = clientIp,
                UserAgent = connInfo.UserAgent,
                Success = true,
                UserId = result.UserId
            });

            _logger.LogInformation("User registered: {UserId} from IP: {Ip}",
                result.UserId, MaskIp(clientIp));

            return Created($"/api/users/{result.UserId}", result);
        }

        /// <summary>
        /// Login (step 1) - verify credentials
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(MfaLoginResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(RateLimitedError), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(RateLimitedError), StatusCodes.Status429TooManyRequests)]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            var connInfo = _connectionInfo.GetConnectionInfo(HttpContext);
            var clientIp = connInfo.IpAddress;

            // Check if IP is blocked
            if (_rateLimiter.IsBlocked(clientIp))
            {
                _logger.LogWarning("Blocked IP attempted login: {Ip}", MaskIp(clientIp));
                return StatusCode(403, new RateLimitedError
                {
                    Error = "Access denied",
                    IsBlocked = true
                });
            }

            // Check rate limit
            var rateResult = _rateLimiter.CheckAndIncrement(clientIp, "login");
            AddRateLimitHeaders(rateResult);

            if (!rateResult.IsAllowed)
            {
                // Mark as suspicious if repeatedly hitting limits
                var recentAttempts = _sessionService.GetRecentLoginAttemptsByIp(clientIp, 20);
                var failedCount = recentAttempts.Count(a => !a.Success);
                if (failedCount >= 15)
                {
                    _rateLimiter.MarkSuspicious(clientIp, TimeSpan.FromHours(24), "Multiple failed login attempts");
                    _logger.LogWarning("IP marked as suspicious due to failed logins: {Ip}", MaskIp(clientIp));
                }

                _logger.LogWarning("Login rate limited for IP: {Ip}", MaskIp(clientIp));
                return TooManyRequests(rateResult, "Too many login attempts");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                RecordFailedLogin(request?.Email, clientIp, connInfo.UserAgent, "Invalid request");
                return Unauthorized(new RateLimitedError { Error = "Invalid credentials" });
            }

            var result = _authService.Login(request.Email, request.Password);

            if (!result.Success && !result.RequiresMfa)
            {
                RecordFailedLogin(request.Email, clientIp, connInfo.UserAgent, "Invalid credentials");
                _logger.LogWarning("Failed login attempt for: {Email} from IP: {Ip}",
                    MaskEmail(request.Email), MaskIp(clientIp));
                return Unauthorized(new RateLimitedError { Error = result.Error ?? "Invalid credentials" });
            }

            // Success or MFA required - reset rate limit for this IP
            if (result.Success && !result.RequiresMfa)
            {
                _rateLimiter.Reset(clientIp, "login");

                // Create session
                var session = _sessionService.CreateSession(
                    result.User.Id,
                    connInfo,
                    TimeSpan.FromHours(24));

                // Record successful login
                _sessionService.RecordLoginAttempt(new LoginAttempt
                {
                    Identifier = request.Email,
                    IpAddress = clientIp,
                    UserAgent = connInfo.UserAgent,
                    Success = true,
                    UserId = result.User.Id
                });

                _logger.LogInformation("User logged in: {UserId} from IP: {Ip}",
                    result.User.Id, MaskIp(clientIp));
            }

            return Ok(result);
        }

        /// <summary>
        /// Login (step 2) - verify MFA code
        /// </summary>
        [HttpPost("login/mfa")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(MfaLoginResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(RateLimitedError), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(RateLimitedError), StatusCodes.Status429TooManyRequests)]
        public IActionResult VerifyMfaLogin([FromBody] MfaLoginRequest request)
        {
            var connInfo = _connectionInfo.GetConnectionInfo(HttpContext);
            var clientIp = connInfo.IpAddress;

            // MFA verification uses login rate limit
            var rateResult = _rateLimiter.Check(clientIp, "login");
            if (!rateResult.IsAllowed)
            {
                AddRateLimitHeaders(rateResult);
                return TooManyRequests(rateResult, "Too many attempts");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.MfaToken) || string.IsNullOrWhiteSpace(request.Code))
            {
                return Unauthorized(new RateLimitedError { Error = "Invalid request" });
            }

            var result = _authService.VerifyMfaLogin(
                request.MfaToken,
                request.Code,
                request.Method,
                request.IsRecoveryCode);

            if (!result.Success)
            {
                // Increment on failure
                _rateLimiter.CheckAndIncrement(clientIp, "login");
                _logger.LogWarning("Failed MFA verification from IP: {Ip}", MaskIp(clientIp));
                return Unauthorized(new RateLimitedError { Error = result.Error ?? "Invalid MFA code" });
            }

            // Success - reset rate limit and create session
            _rateLimiter.Reset(clientIp, "login");

            var session = _sessionService.CreateSession(
                result.User.Id,
                connInfo,
                TimeSpan.FromHours(24));

            _sessionService.RecordLoginAttempt(new LoginAttempt
            {
                Identifier = result.User.Email,
                IpAddress = clientIp,
                UserAgent = connInfo.UserAgent,
                Success = true,
                UserId = result.User.Id
            });

            _logger.LogInformation("User logged in with MFA: {UserId} from IP: {Ip}",
                result.User.Id, MaskIp(clientIp));

            return Ok(result);
        }

        /// <summary>
        /// Get current user's sessions
        /// </summary>
        [HttpGet("sessions")]
        [Authorize]
        [ProducesResponseType(typeof(List<UserSessionDto>), StatusCodes.Status200OK)]
        public IActionResult GetSessions()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var sessions = _sessionService.GetUserSessions(userId.Value);
            var currentFingerprint = _connectionInfo.GenerateFingerprint(HttpContext);

            var dtos = sessions.Select(s => new UserSessionDto
            {
                SessionId = MaskSessionId(s.SessionId),
                IpAddress = MaskIp(s.IpAddress),
                DeviceType = s.DeviceType,
                Browser = s.Browser,
                Location = s.Location,
                CreatedAt = s.CreatedAt,
                LastActivityAt = s.LastActivityAt,
                IsCurrent = s.Fingerprint == currentFingerprint
            }).ToList();

            return Ok(dtos);
        }

        /// <summary>
        /// Logout from current session
        /// </summary>
        [HttpPost("logout")]
        [Authorize]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public IActionResult Logout()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            // Get current session by fingerprint
            var fingerprint = _connectionInfo.GenerateFingerprint(HttpContext);
            var sessions = _sessionService.GetUserSessions(userId.Value);
            var currentSession = sessions.FirstOrDefault(s => s.Fingerprint == fingerprint);

            if (currentSession != null)
            {
                _sessionService.InvalidateSession(currentSession.SessionId);
            }

            return Ok(new { message = "Logged out successfully" });
        }

        /// <summary>
        /// Logout from all sessions
        /// </summary>
        [HttpPost("logout/all")]
        [Authorize]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public IActionResult LogoutAll()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            _sessionService.InvalidateAllSessions(userId.Value);

            return Ok(new { message = "Logged out from all sessions" });
        }

        /// <summary>
        /// Get connection info
        /// </summary>
        [HttpGet("connection-info")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ConnectionInfoDto), StatusCodes.Status200OK)]
        public IActionResult GetConnectionInfo()
        {
            var connInfo = _connectionInfo.GetConnectionInfo(HttpContext);

            return Ok(new ConnectionInfoDto
            {
                IpAddress = MaskIp(connInfo.IpAddress),
                IsProxied = connInfo.IsProxied,
                DeviceType = connInfo.UserAgentInfo?.DeviceType,
                Browser = connInfo.UserAgentInfo?.Browser,
                BrowserVersion = connInfo.UserAgentInfo?.BrowserVersion,
                OS = connInfo.UserAgentInfo?.OS,
                IsMobile = connInfo.UserAgentInfo?.IsMobile ?? false,
                IsBot = connInfo.UserAgentInfo?.IsBot ?? false,
                IsSecure = connInfo.IsSecure
            });
        }

        /// <summary>
        /// Get recent login activity
        /// </summary>
        [HttpGet("login-history")]
        [Authorize]
        [ProducesResponseType(typeof(List<LoginAttemptDto>), StatusCodes.Status200OK)]
        public IActionResult GetLoginHistory([FromQuery] int count = 10)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            count = Math.Min(count, 50); // Max 50

            var attempts = _sessionService.GetUserLoginHistory(userId.Value, count);

            var dtos = attempts.Select(a => new LoginAttemptDto
            {
                IpAddress = MaskIp(a.IpAddress),
                Timestamp = a.Timestamp,
                Success = a.Success,
                FailureReason = a.FailureReason
            }).ToList();

            return Ok(dtos);
        }

        #region Helper Methods

        private void RecordFailedLogin(string identifier, string ip, string userAgent, string reason)
        {
            _sessionService.RecordLoginAttempt(new LoginAttempt
            {
                Identifier = identifier,
                IpAddress = ip,
                UserAgent = userAgent,
                Success = false,
                FailureReason = reason
            });
        }

        private void AddRateLimitHeaders(RateLimitResult result)
        {
            Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
            Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
            Response.Headers["X-RateLimit-Reset"] = result.WindowReset.ToString("o");
        }

        private IActionResult TooManyRequests(RateLimitResult result, string message)
        {
            Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();

            return StatusCode(429, new RateLimitedError
            {
                Error = message,
                RetryAfterSeconds = result.RetryAfterSeconds,
                Limit = result.Limit,
                Remaining = result.Remaining
            });
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? User?.FindFirst("sub")?.Value;

            if (int.TryParse(userIdClaim, out int userId))
                return userId;
            return null;
        }

        private static string MaskIp(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return null;

            // For IPv4, mask last octet
            var parts = ip.Split('.');
            if (parts.Length == 4)
            {
                return $"{parts[0]}.{parts[1]}.{parts[2]}.*";
            }

            // For IPv6, mask last 64 bits
            if (ip.Contains(':'))
            {
                var colonIndex = ip.LastIndexOf(':');
                if (colonIndex > 0)
                {
                    return ip.Substring(0, colonIndex) + ":*";
                }
            }

            return ip;
        }

        private static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
                return "***";

            var parts = email.Split('@');
            var local = parts[0];
            var domain = parts[1];

            if (local.Length <= 2)
                return $"{local[0]}***@{domain}";

            return $"{local[0]}***{local[local.Length - 1]}@{domain}";
        }

        private static string MaskSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || sessionId.Length < 8)
                return "***";

            return sessionId.Substring(0, 4) + "..." + sessionId.Substring(sessionId.Length - 4);
        }

        #endregion
    }

    #region DTOs

    /// <summary>
    /// Error response with rate limit info
    /// </summary>
    public class RateLimitedError
    {
        public string Error { get; set; }
        public int? RetryAfterSeconds { get; set; }
        public int? Limit { get; set; }
        public int? Remaining { get; set; }
        public bool IsBlocked { get; set; }
    }

    /// <summary>
    /// User session DTO
    /// </summary>
    public class UserSessionDto
    {
        public string SessionId { get; set; }
        public string IpAddress { get; set; }
        public string DeviceType { get; set; }
        public string Browser { get; set; }
        public string Location { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public bool IsCurrent { get; set; }
    }

    /// <summary>
    /// Connection info DTO
    /// </summary>
    public class ConnectionInfoDto
    {
        public string IpAddress { get; set; }
        public bool IsProxied { get; set; }
        public string DeviceType { get; set; }
        public string Browser { get; set; }
        public string BrowserVersion { get; set; }
        public string OS { get; set; }
        public bool IsMobile { get; set; }
        public bool IsBot { get; set; }
        public bool IsSecure { get; set; }
    }

    /// <summary>
    /// Login attempt DTO
    /// </summary>
    public class LoginAttemptDto
    {
        public string IpAddress { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string FailureReason { get; set; }
    }

    #endregion
}
