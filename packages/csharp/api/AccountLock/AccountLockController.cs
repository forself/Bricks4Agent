using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using Bricks4Agent.Security.AccountLock;
using Bricks4Agent.Security.AccountLock.Models;

namespace Bricks4Agent.Api.AccountLock
{
    /// <summary>
    /// Account lock management API
    /// </summary>
    [ApiController]
    [Route("api/account-locks")]
    [Produces("application/json")]
    [Authorize]
    public class AccountLockController : ControllerBase
    {
        private readonly IAccountLockService _lockService;
        private readonly ILogger<AccountLockController> _logger;

        public AccountLockController(
            IAccountLockService lockService,
            ILogger<AccountLockController> logger)
        {
            _lockService = lockService;
            _logger = logger;
        }

        #region Current User Endpoints

        /// <summary>
        /// Get current user's lock status
        /// </summary>
        /// <returns>Lock status</returns>
        [HttpGet("my/status")]
        [ProducesResponseType(typeof(UserLockStatus), StatusCodes.Status200OK)]
        public IActionResult GetMyLockStatus()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var status = _lockService.GetUserLockStatus(userId.Value);
            return Ok(status);
        }

        /// <summary>
        /// Get current user's lock history
        /// </summary>
        /// <param name="limit">Max records to return</param>
        /// <returns>Lock history</returns>
        [HttpGet("my/history")]
        [ProducesResponseType(typeof(List<LockHistoryEntry>), StatusCodes.Status200OK)]
        public IActionResult GetMyLockHistory([FromQuery] int limit = 20)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var history = _lockService.GetLockHistory(userId.Value, Math.Clamp(limit, 1, 100));
            return Ok(history);
        }

        #endregion

        #region Admin - Account Lock Management

        /// <summary>
        /// Check if a user is locked (admin)
        /// </summary>
        /// <param name="userId">User ID to check</param>
        /// <returns>Lock check result</returns>
        [HttpGet("users/{userId}/check")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(LockCheckResult), StatusCodes.Status200OK)]
        public IActionResult CheckUserLock(int userId)
        {
            var result = _lockService.CheckLock(userId);
            return Ok(result);
        }

        /// <summary>
        /// Get user's lock status (admin)
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>Lock status</returns>
        [HttpGet("users/{userId}/status")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(UserLockStatus), StatusCodes.Status200OK)]
        public IActionResult GetUserLockStatus(int userId)
        {
            var status = _lockService.GetUserLockStatus(userId);
            return Ok(status);
        }

        /// <summary>
        /// Get user's lock history (admin)
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="limit">Max records</param>
        /// <returns>Lock history</returns>
        [HttpGet("users/{userId}/history")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(List<LockHistoryEntry>), StatusCodes.Status200OK)]
        public IActionResult GetUserLockHistory(int userId, [FromQuery] int limit = 50)
        {
            var history = _lockService.GetLockHistory(userId, Math.Clamp(limit, 1, 200));
            return Ok(history);
        }

        /// <summary>
        /// Lock a user account (admin)
        /// </summary>
        /// <param name="userId">User ID to lock</param>
        /// <param name="request">Lock request details</param>
        /// <returns>Created lock record</returns>
        [HttpPost("users/{userId}/lock")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(AccountLock), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public IActionResult LockUser(int userId, [FromBody] LockUserRequest request)
        {
            var adminUserId = GetCurrentUserId();
            var adminUsername = User?.Identity?.Name;

            if (adminUserId == userId)
            {
                return BadRequest(new ApiError { Error = "Cannot lock your own account" });
            }

            var lockRequest = new LockAccountRequest
            {
                UserId = userId,
                LockType = request?.LockType ?? LockType.Manual,
                Scope = request?.Scope ?? LockScope.Account,
                Reason = request?.Reason ?? "Locked by administrator",
                Description = request?.Description,
                DurationMinutes = request?.DurationMinutes,
                InvalidateSessions = request?.InvalidateSessions ?? false,
                NotifyUser = request?.NotifyUser ?? true
            };

            var lockRecord = _lockService.LockAccount(lockRequest, adminUserId, adminUsername);

            _logger.LogWarning("User {UserId} locked by admin {AdminId}. Reason: {Reason}. Duration: {Duration}",
                userId, adminUserId, lockRequest.Reason,
                lockRequest.DurationMinutes.HasValue ? $"{lockRequest.DurationMinutes} minutes" : "permanent");

            return Created($"/api/account-locks/{lockRecord.Id}", lockRecord);
        }

        /// <summary>
        /// Unlock a user account (admin)
        /// </summary>
        /// <param name="userId">User ID to unlock</param>
        /// <param name="request">Unlock request details</param>
        /// <returns>Number of locks removed</returns>
        [HttpPost("users/{userId}/unlock")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(UnlockResponse), StatusCodes.Status200OK)]
        public IActionResult UnlockUser(int userId, [FromBody] UnlockUserRequest request)
        {
            var adminUserId = GetCurrentUserId();
            var adminUsername = User?.Identity?.Name;

            var unlockRequest = new UnlockAccountRequest
            {
                UserId = userId,
                LockId = request?.LockId,
                Scope = request?.Scope,
                Reason = request?.Reason ?? "Unlocked by administrator"
            };

            var count = _lockService.UnlockAccount(unlockRequest, adminUserId ?? 0, adminUsername);

            _logger.LogInformation("User {UserId} unlocked by admin {AdminId}. {Count} lock(s) removed",
                userId, adminUserId, count);

            return Ok(new UnlockResponse { UnlockedCount = count });
        }

        /// <summary>
        /// Get all active account locks (admin)
        /// </summary>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <returns>List of active locks</returns>
        [HttpGet("active")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(List<AccountLockRecord>), StatusCodes.Status200OK)]
        public IActionResult GetActiveLocks([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var locks = _lockService.GetAllActiveLocks(page, pageSize);
            return Ok(locks);
        }

        /// <summary>
        /// Get lock by ID (admin)
        /// </summary>
        /// <param name="lockId">Lock ID</param>
        /// <returns>Lock details</returns>
        [HttpGet("{lockId}")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(AccountLock), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetLock(long lockId)
        {
            var lockRecord = _lockService.GetUserLockStatus(0); // TODO: Get by lock ID
            return NotFound(new ApiError { Error = "Lock not found" });
        }

        /// <summary>
        /// Unlock by lock ID (admin)
        /// </summary>
        /// <param name="lockId">Lock ID</param>
        /// <param name="request">Unlock reason</param>
        /// <returns>Success status</returns>
        [HttpDelete("{lockId}")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult UnlockById(long lockId, [FromBody] UnlockReasonRequest request)
        {
            var adminUserId = GetCurrentUserId();
            var adminUsername = User?.Identity?.Name;

            var success = _lockService.UnlockById(lockId, adminUserId ?? 0, adminUsername, request?.Reason);

            if (!success)
            {
                return NotFound(new ApiError { Error = "Lock not found or already inactive" });
            }

            _logger.LogInformation("Lock {LockId} removed by admin {AdminId}", lockId, adminUserId);

            return Ok(new { message = "Lock removed successfully" });
        }

        #endregion

        #region Admin - IP Lock Management

        /// <summary>
        /// Check if an IP is locked (admin)
        /// </summary>
        /// <param name="ipAddress">IP address to check</param>
        /// <returns>Lock check result</returns>
        [HttpGet("ips/check")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(LockCheckResult), StatusCodes.Status200OK)]
        public IActionResult CheckIpLock([FromQuery] string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
            {
                return BadRequest(new ApiError { Error = "IP address is required" });
            }

            var result = _lockService.CheckIpLock(ipAddress);
            return Ok(result);
        }

        /// <summary>
        /// Lock an IP address (admin)
        /// </summary>
        /// <param name="request">Lock request</param>
        /// <returns>Created lock record</returns>
        [HttpPost("ips/lock")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(IpLock), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public IActionResult LockIp([FromBody] LockIpApiRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.IpAddress))
            {
                return BadRequest(new ApiError { Error = "IP address is required" });
            }

            var adminUserId = GetCurrentUserId();

            var lockRequest = new LockIpRequest
            {
                IpAddress = request.IpAddress,
                LockType = request.LockType ?? LockType.Manual,
                Reason = request.Reason ?? "Blocked by administrator",
                DurationMinutes = request.DurationMinutes
            };

            var lockRecord = _lockService.LockIp(lockRequest, adminUserId);

            _logger.LogWarning("IP {IpAddress} locked by admin {AdminId}. Reason: {Reason}",
                request.IpAddress, adminUserId, lockRequest.Reason);

            return Created($"/api/account-locks/ips/{lockRecord.Id}", lockRecord);
        }

        /// <summary>
        /// Unlock an IP address (admin)
        /// </summary>
        /// <param name="request">Unlock request</param>
        /// <returns>Success status</returns>
        [HttpPost("ips/unlock")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult UnlockIp([FromBody] UnlockIpRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.IpAddress))
            {
                return BadRequest(new ApiError { Error = "IP address is required" });
            }

            var adminUserId = GetCurrentUserId();
            var adminUsername = User?.Identity?.Name;

            var success = _lockService.UnlockIp(request.IpAddress, adminUserId ?? 0, adminUsername);

            if (!success)
            {
                return NotFound(new ApiError { Error = "IP lock not found" });
            }

            _logger.LogInformation("IP {IpAddress} unlocked by admin {AdminId}", request.IpAddress, adminUserId);

            return Ok(new { message = "IP unlocked successfully" });
        }

        /// <summary>
        /// Get all active IP locks (admin)
        /// </summary>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <returns>List of active IP locks</returns>
        [HttpGet("ips/active")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(List<IpLock>), StatusCodes.Status200OK)]
        public IActionResult GetActiveIpLocks([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var locks = _lockService.GetAllActiveIpLocks(page, pageSize);
            return Ok(locks);
        }

        /// <summary>
        /// Unlock IP by lock ID (admin)
        /// </summary>
        /// <param name="lockId">Lock ID</param>
        /// <returns>Success status</returns>
        [HttpDelete("ips/{lockId}")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult UnlockIpById(long lockId)
        {
            var adminUserId = GetCurrentUserId();
            var adminUsername = User?.Identity?.Name;

            var success = _lockService.UnlockIpById(lockId, adminUserId ?? 0, adminUsername);

            if (!success)
            {
                return NotFound(new ApiError { Error = "IP lock not found or already inactive" });
            }

            _logger.LogInformation("IP lock {LockId} removed by admin {AdminId}", lockId, adminUserId);

            return Ok(new { message = "IP lock removed successfully" });
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Get lock statistics (admin)
        /// </summary>
        /// <param name="days">Number of days</param>
        /// <returns>Lock statistics</returns>
        [HttpGet("statistics")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(LockStatistics), StatusCodes.Status200OK)]
        public IActionResult GetStatistics([FromQuery] int days = 7)
        {
            var stats = _lockService.GetStatistics(Math.Clamp(days, 1, 90));
            return Ok(stats);
        }

        #endregion

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
    /// Lock user request
    /// </summary>
    public class LockUserRequest
    {
        /// <summary>
        /// Type of lock
        /// </summary>
        public LockType? LockType { get; set; }

        /// <summary>
        /// Scope of lock
        /// </summary>
        public LockScope? Scope { get; set; }

        /// <summary>
        /// Reason for locking
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Detailed description
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Duration in minutes (null = permanent)
        /// </summary>
        public int? DurationMinutes { get; set; }

        /// <summary>
        /// Whether to invalidate all sessions
        /// </summary>
        public bool? InvalidateSessions { get; set; }

        /// <summary>
        /// Whether to notify the user
        /// </summary>
        public bool? NotifyUser { get; set; }
    }

    /// <summary>
    /// Unlock user request
    /// </summary>
    public class UnlockUserRequest
    {
        /// <summary>
        /// Specific lock ID to remove
        /// </summary>
        public long? LockId { get; set; }

        /// <summary>
        /// Scope to unlock (null = all)
        /// </summary>
        public LockScope? Scope { get; set; }

        /// <summary>
        /// Reason for unlocking
        /// </summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// Unlock reason request
    /// </summary>
    public class UnlockReasonRequest
    {
        public string Reason { get; set; }
    }

    /// <summary>
    /// Lock IP API request
    /// </summary>
    public class LockIpApiRequest
    {
        /// <summary>
        /// IP address to lock
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// Type of lock
        /// </summary>
        public LockType? LockType { get; set; }

        /// <summary>
        /// Reason for locking
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Duration in minutes
        /// </summary>
        public int? DurationMinutes { get; set; }
    }

    /// <summary>
    /// Unlock IP request
    /// </summary>
    public class UnlockIpRequest
    {
        /// <summary>
        /// IP address to unlock
        /// </summary>
        public string IpAddress { get; set; }
    }

    /// <summary>
    /// Unlock response
    /// </summary>
    public class UnlockResponse
    {
        public int UnlockedCount { get; set; }
    }

    /// <summary>
    /// API error
    /// </summary>
    public class ApiError
    {
        public string Error { get; set; }
    }

    #endregion
}
