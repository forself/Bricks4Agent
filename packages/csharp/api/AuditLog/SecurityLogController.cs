using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using Bricks4Agent.Security.AuditLog;
using Bricks4Agent.Security.AuditLog.Models;

namespace Bricks4Agent.Api.AuditLog
{
    /// <summary>
    /// Security log management and query API
    /// </summary>
    [ApiController]
    [Route("api/security-logs")]
    [Produces("application/json")]
    [Authorize]
    public class SecurityLogController : ControllerBase
    {
        private readonly ISecurityLogService _logService;
        private readonly ILogger<SecurityLogController> _logger;

        public SecurityLogController(
            ISecurityLogService logService,
            ILogger<SecurityLogController> logger)
        {
            _logService = logService;
            _logger = logger;
        }

        /// <summary>
        /// Query security logs with filters
        /// </summary>
        /// <param name="request">Query parameters</param>
        /// <returns>Paged log results</returns>
        [HttpPost("query")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(SecurityLogResult), StatusCodes.Status200OK)]
        public IActionResult QueryLogs([FromBody] SecurityLogQueryRequest request)
        {
            var query = new SecurityLogQuery
            {
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                EventTypes = request.EventTypes,
                Severities = request.Severities,
                Outcome = request.Outcome,
                UserId = request.UserId,
                Username = request.Username,
                IpAddressHash = request.IpAddressHash,
                SessionId = request.SessionId,
                CorrelationId = request.CorrelationId,
                SearchText = request.SearchText,
                Tags = request.Tags,
                CountryCode = request.CountryCode,
                Page = request.Page > 0 ? request.Page : 1,
                PageSize = Math.Clamp(request.PageSize, 1, 100),
                SortBy = request.SortBy ?? "Timestamp",
                SortDescending = request.SortDescending ?? true
            };

            var result = _logService.Query(query);

            _logger.LogInformation("Security logs queried by user {UserId}, returned {Count} results",
                GetCurrentUserId(), result.Items.Count);

            return Ok(result);
        }

        /// <summary>
        /// Get security log by ID
        /// </summary>
        /// <param name="id">Log entry ID</param>
        /// <returns>Log entry details</returns>
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(SecurityLogEntry), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetLog(long id)
        {
            var entry = _logService.GetById(id);
            if (entry == null)
            {
                return NotFound(new { error = "Log entry not found" });
            }

            return Ok(entry);
        }

        /// <summary>
        /// Get current user's login history
        /// </summary>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <returns>Login history</returns>
        [HttpGet("my/login-history")]
        [ProducesResponseType(typeof(LoginHistoryResult), StatusCodes.Status200OK)]
        public IActionResult GetMyLoginHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var result = _logService.GetUserLoginHistory(userId.Value, page, pageSize);
            return Ok(result);
        }

        /// <summary>
        /// Get current user's activity summary
        /// </summary>
        /// <param name="days">Number of days to analyze</param>
        /// <returns>Activity summary</returns>
        [HttpGet("my/activity")]
        [ProducesResponseType(typeof(UserActivitySummary), StatusCodes.Status200OK)]
        public IActionResult GetMyActivity([FromQuery] int days = 30)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
            var result = _logService.GetUserActivity(userId.Value, since);
            return Ok(result);
        }

        /// <summary>
        /// Get current user's recent failed logins
        /// </summary>
        /// <param name="count">Number of records</param>
        /// <returns>Failed login records</returns>
        [HttpGet("my/failed-logins")]
        [ProducesResponseType(typeof(List<LoginRecord>), StatusCodes.Status200OK)]
        public IActionResult GetMyFailedLogins([FromQuery] int count = 10)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var result = _logService.GetRecentFailedLogins(userId.Value, Math.Clamp(count, 1, 50));
            return Ok(result);
        }

        /// <summary>
        /// Get any user's login history (admin only)
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <returns>Login history</returns>
        [HttpGet("users/{userId}/login-history")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(LoginHistoryResult), StatusCodes.Status200OK)]
        public IActionResult GetUserLoginHistory(int userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = _logService.GetUserLoginHistory(userId, page, pageSize);
            return Ok(result);
        }

        /// <summary>
        /// Get any user's activity summary (admin only)
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="days">Number of days</param>
        /// <returns>Activity summary</returns>
        [HttpGet("users/{userId}/activity")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(UserActivitySummary), StatusCodes.Status200OK)]
        public IActionResult GetUserActivity(int userId, [FromQuery] int days = 30)
        {
            var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
            var result = _logService.GetUserActivity(userId, since);
            return Ok(result);
        }

        /// <summary>
        /// Get IP activity summary (admin only)
        /// </summary>
        /// <param name="ipHash">IP address hash</param>
        /// <param name="days">Number of days</param>
        /// <returns>IP activity summary</returns>
        [HttpGet("ips/{ipHash}/activity")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(IpActivitySummary), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetIpActivity(string ipHash, [FromQuery] int days = 30)
        {
            var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
            var result = _logService.GetIpActivity(ipHash, since);

            if (result == null)
            {
                return NotFound(new { error = "No activity found for this IP" });
            }

            return Ok(result);
        }

        /// <summary>
        /// Get suspicious login records (admin only)
        /// </summary>
        /// <param name="hours">Hours to look back</param>
        /// <param name="count">Max records</param>
        /// <returns>Suspicious login records</returns>
        [HttpGet("suspicious-logins")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(List<LoginRecord>), StatusCodes.Status200OK)]
        public IActionResult GetSuspiciousLogins([FromQuery] int hours = 24, [FromQuery] int count = 100)
        {
            var since = DateTime.UtcNow.AddHours(-Math.Clamp(hours, 1, 168)); // Max 7 days
            var result = _logService.GetSuspiciousLogins(since, Math.Clamp(count, 1, 500));
            return Ok(result);
        }

        /// <summary>
        /// Get security statistics (admin only)
        /// </summary>
        /// <param name="days">Number of days</param>
        /// <returns>Security statistics</returns>
        [HttpGet("statistics")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(SecurityStatistics), StatusCodes.Status200OK)]
        public IActionResult GetStatistics([FromQuery] int days = 7)
        {
            var result = _logService.GetStatistics(Math.Clamp(days, 1, 90));
            return Ok(result);
        }

        /// <summary>
        /// Get security statistics for date range (admin only)
        /// </summary>
        /// <param name="request">Date range</param>
        /// <returns>Security statistics</returns>
        [HttpPost("statistics")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(SecurityStatistics), StatusCodes.Status200OK)]
        public IActionResult GetStatisticsForRange([FromBody] DateRangeRequest request)
        {
            var startDate = request.StartDate ?? DateTime.UtcNow.AddDays(-7);
            var endDate = request.EndDate ?? DateTime.UtcNow;

            // Limit range to 90 days
            if ((endDate - startDate).TotalDays > 90)
            {
                startDate = endDate.AddDays(-90);
            }

            var result = _logService.GetStatistics(startDate, endDate);
            return Ok(result);
        }

        /// <summary>
        /// Get unacknowledged security alerts (admin only)
        /// </summary>
        /// <returns>List of alerts</returns>
        [HttpGet("alerts")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(List<SecurityAlert>), StatusCodes.Status200OK)]
        public IActionResult GetAlerts()
        {
            var result = _logService.GetUnacknowledgedAlerts();
            return Ok(result);
        }

        /// <summary>
        /// Acknowledge a security alert (admin only)
        /// </summary>
        /// <param name="alertId">Alert ID</param>
        /// <param name="request">Acknowledgement details</param>
        /// <returns>Success status</returns>
        [HttpPost("alerts/{alertId}/acknowledge")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult AcknowledgeAlert(long alertId, [FromBody] AcknowledgeAlertRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            _logService.AcknowledgeAlert(alertId, userId.Value, request?.Note);

            _logger.LogInformation("Alert {AlertId} acknowledged by user {UserId}", alertId, userId.Value);

            return Ok(new { message = "Alert acknowledged" });
        }

        /// <summary>
        /// Delete user's logs (GDPR - admin only)
        /// </summary>
        /// <param name="userId">User ID to delete logs for</param>
        /// <returns>Number of deleted records</returns>
        [HttpDelete("users/{userId}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(DeleteLogsResponse), StatusCodes.Status200OK)]
        public IActionResult DeleteUserLogs(int userId)
        {
            var adminUserId = GetCurrentUserId();

            var count = _logService.DeleteUserLogs(userId);

            _logger.LogWarning("User {AdminId} deleted {Count} log entries for user {UserId}",
                adminUserId, count, userId);

            // Log this action
            _logService.LogAdminAction(
                adminUserId ?? 0,
                User?.Identity?.Name,
                "DeleteUserLogs",
                $"User:{userId}",
                GetClientIp(),
                $"Deleted {count} log entries");

            return Ok(new DeleteLogsResponse { DeletedCount = count });
        }

        /// <summary>
        /// Export logs to CSV (admin only)
        /// </summary>
        /// <param name="request">Query parameters</param>
        /// <returns>CSV file</returns>
        [HttpPost("export")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        public IActionResult ExportLogs([FromBody] SecurityLogQueryRequest request)
        {
            // Limit export to 10000 records
            var query = new SecurityLogQuery
            {
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                EventTypes = request.EventTypes,
                Severities = request.Severities,
                Outcome = request.Outcome,
                UserId = request.UserId,
                Page = 1,
                PageSize = 10000,
                SortDescending = true
            };

            var result = _logService.Query(query);

            // Generate CSV
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Timestamp,EventType,Severity,Outcome,UserId,Username,IpAddress,Message,UserAgent,SessionId");

            foreach (var entry in result.Items)
            {
                csv.AppendLine($"\"{entry.Timestamp:O}\",\"{entry.EventType}\",\"{entry.Severity}\",\"{entry.Outcome}\",\"{entry.UserId}\",\"{EscapeCsv(entry.Username)}\",\"{EscapeCsv(entry.IpAddress)}\",\"{EscapeCsv(entry.Message)}\",\"{EscapeCsv(entry.UserAgent)}\",\"{entry.SessionId}\"");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            var fileName = $"security-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

            _logger.LogInformation("User {UserId} exported {Count} security logs",
                GetCurrentUserId(), result.Items.Count);

            // Log this action
            _logService.LogAdminAction(
                GetCurrentUserId() ?? 0,
                User?.Identity?.Name,
                "ExportLogs",
                "SecurityLogs",
                GetClientIp(),
                $"Exported {result.Items.Count} log entries");

            return File(bytes, "text/csv", fileName);
        }

        #region Dashboard Endpoints

        /// <summary>
        /// Get dashboard summary (admin only)
        /// </summary>
        /// <returns>Dashboard data</returns>
        [HttpGet("dashboard")]
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [ProducesResponseType(typeof(DashboardSummary), StatusCodes.Status200OK)]
        public IActionResult GetDashboard()
        {
            var stats24h = _logService.GetStatistics(1);
            var stats7d = _logService.GetStatistics(7);
            var alerts = _logService.GetUnacknowledgedAlerts();
            var suspiciousLogins = _logService.GetSuspiciousLogins(DateTime.UtcNow.AddHours(-24), 10);

            return Ok(new DashboardSummary
            {
                // Last 24 hours
                TotalEvents24h = stats24h.ByEventType.Values.Sum(),
                LoginAttempts24h = stats24h.TotalLoginAttempts,
                FailedLogins24h = stats24h.FailedLogins,
                SuspiciousActivities24h = stats24h.SuspiciousActivities,
                RateLimitHits24h = stats24h.RateLimitExceeded,

                // Last 7 days
                TotalEvents7d = stats7d.ByEventType.Values.Sum(),
                LoginAttempts7d = stats7d.TotalLoginAttempts,
                FailedLogins7d = stats7d.FailedLogins,
                NewAccounts7d = stats7d.NewAccounts,

                // Current status
                UnacknowledgedAlerts = alerts.Count,
                RecentSuspiciousLogins = suspiciousLogins,

                // Trends
                LoginsByHour = stats24h.ByHourOfDay,
                LoginsByCountry = stats7d.ByCountry,
                TopFailedLoginIps = stats7d.TopFailedLoginIps,

                // Success rate
                LoginSuccessRate24h = stats24h.LoginSuccessRate,
                LoginSuccessRate7d = stats7d.LoginSuccessRate
            });
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

        private string GetClientIp()
        {
            return HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                ?? HttpContext.Request.Headers["X-Real-IP"].FirstOrDefault()
                ?? HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("\"", "\"\"").Replace("\r", "").Replace("\n", " ");
        }

        #endregion
    }

    #region Request/Response DTOs

    /// <summary>
    /// Security log query request
    /// </summary>
    public class SecurityLogQueryRequest
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<SecurityEventType> EventTypes { get; set; }
        public List<SecuritySeverity> Severities { get; set; }
        public EventOutcome? Outcome { get; set; }
        public int? UserId { get; set; }
        public string Username { get; set; }
        public string IpAddressHash { get; set; }
        public string SessionId { get; set; }
        public string CorrelationId { get; set; }
        public string SearchText { get; set; }
        public List<string> Tags { get; set; }
        public string CountryCode { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string SortBy { get; set; }
        public bool? SortDescending { get; set; }
    }

    /// <summary>
    /// Date range request
    /// </summary>
    public class DateRangeRequest
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    /// <summary>
    /// Acknowledge alert request
    /// </summary>
    public class AcknowledgeAlertRequest
    {
        public string Note { get; set; }
    }

    /// <summary>
    /// Delete logs response
    /// </summary>
    public class DeleteLogsResponse
    {
        public int DeletedCount { get; set; }
    }

    /// <summary>
    /// Dashboard summary
    /// </summary>
    public class DashboardSummary
    {
        // Last 24 hours
        public int TotalEvents24h { get; set; }
        public int LoginAttempts24h { get; set; }
        public int FailedLogins24h { get; set; }
        public int SuspiciousActivities24h { get; set; }
        public int RateLimitHits24h { get; set; }

        // Last 7 days
        public int TotalEvents7d { get; set; }
        public int LoginAttempts7d { get; set; }
        public int FailedLogins7d { get; set; }
        public int NewAccounts7d { get; set; }

        // Current status
        public int UnacknowledgedAlerts { get; set; }
        public List<LoginRecord> RecentSuspiciousLogins { get; set; }

        // Trends
        public Dictionary<int, int> LoginsByHour { get; set; }
        public Dictionary<string, int> LoginsByCountry { get; set; }
        public List<IpActivitySummary> TopFailedLoginIps { get; set; }

        // Success rates
        public double LoginSuccessRate24h { get; set; }
        public double LoginSuccessRate7d { get; set; }
    }

    #endregion
}
