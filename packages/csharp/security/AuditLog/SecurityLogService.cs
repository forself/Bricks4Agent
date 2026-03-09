using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bricks4Agent.Security.AuditLog.Models;

namespace Bricks4Agent.Security.AuditLog
{
    /// <summary>
    /// Security log service interface
    /// </summary>
    public interface ISecurityLogService
    {
        /// <summary>
        /// Log a security event
        /// </summary>
        void Log(SecurityLogEntry entry);

        /// <summary>
        /// Log a security event with builder
        /// </summary>
        void Log(Action<SecurityLogEntryBuilder> configure);

        /// <summary>
        /// Log a login attempt
        /// </summary>
        void LogLogin(LoginRecord record);

        /// <summary>
        /// Log a successful login
        /// </summary>
        void LogLoginSuccess(int userId, string username, string ipAddress, string userAgent,
            string sessionId = null, bool mfaVerified = false, string mfaMethod = null);

        /// <summary>
        /// Log a failed login
        /// </summary>
        void LogLoginFailed(string username, string ipAddress, string userAgent, string reason);

        /// <summary>
        /// Log a logout
        /// </summary>
        void LogLogout(int userId, string username, string ipAddress, string sessionId = null);

        /// <summary>
        /// Log MFA event
        /// </summary>
        void LogMfaEvent(SecurityEventType eventType, int userId, string username, string ipAddress,
            bool success, string method = null, string details = null);

        /// <summary>
        /// Log account event
        /// </summary>
        void LogAccountEvent(SecurityEventType eventType, int userId, string username, string ipAddress,
            string details = null);

        /// <summary>
        /// Log security event (rate limit, suspicious activity, etc.)
        /// </summary>
        void LogSecurityEvent(SecurityEventType eventType, string ipAddress, string userAgent,
            string message, SecuritySeverity severity = SecuritySeverity.Medium, string details = null);

        /// <summary>
        /// Log admin action
        /// </summary>
        void LogAdminAction(int adminUserId, string adminUsername, string action, string targetResource,
            string ipAddress, string details = null);

        /// <summary>
        /// Query security logs
        /// </summary>
        SecurityLogResult Query(SecurityLogQuery query);

        /// <summary>
        /// Get log by ID
        /// </summary>
        SecurityLogEntry GetById(long id);

        /// <summary>
        /// Get user's login history
        /// </summary>
        LoginHistoryResult GetUserLoginHistory(int userId, int page = 1, int pageSize = 20);

        /// <summary>
        /// Get IP's login history
        /// </summary>
        LoginHistoryResult GetIpLoginHistory(string ipAddress, int page = 1, int pageSize = 20);

        /// <summary>
        /// Get recent failed logins for user
        /// </summary>
        List<LoginRecord> GetRecentFailedLogins(int userId, int count = 10);

        /// <summary>
        /// Get recent failed logins for IP
        /// </summary>
        List<LoginRecord> GetRecentFailedLoginsByIp(string ipAddress, int count = 10);

        /// <summary>
        /// Get suspicious logins
        /// </summary>
        List<LoginRecord> GetSuspiciousLogins(DateTime since, int count = 100);

        /// <summary>
        /// Get user activity summary
        /// </summary>
        UserActivitySummary GetUserActivity(int userId, DateTime? since = null);

        /// <summary>
        /// Get IP activity summary
        /// </summary>
        IpActivitySummary GetIpActivity(string ipAddress, DateTime? since = null);

        /// <summary>
        /// Get security statistics
        /// </summary>
        SecurityStatistics GetStatistics(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Get statistics for last N days
        /// </summary>
        SecurityStatistics GetStatistics(int days = 7);

        /// <summary>
        /// Check if device is new for user
        /// </summary>
        bool IsNewDevice(int userId, string fingerprint);

        /// <summary>
        /// Delete logs by user ID (GDPR)
        /// </summary>
        int DeleteUserLogs(int userId);

        /// <summary>
        /// Get unacknowledged alerts
        /// </summary>
        List<SecurityAlert> GetUnacknowledgedAlerts();

        /// <summary>
        /// Acknowledge alert
        /// </summary>
        void AcknowledgeAlert(long alertId, int userId, string note);
    }

    /// <summary>
    /// Security log service implementation
    /// </summary>
    public class SecurityLogService : ISecurityLogService
    {
        private readonly ISecurityLogRepository _logRepository;
        private readonly ILoginRecordRepository _loginRepository;
        private readonly ISecurityAlertRepository _alertRepository;
        private readonly SecurityLogOptions _options;

        public SecurityLogService(
            ISecurityLogRepository logRepository,
            ILoginRecordRepository loginRepository,
            ISecurityAlertRepository alertRepository = null,
            SecurityLogOptions options = null)
        {
            _logRepository = logRepository ?? throw new ArgumentNullException(nameof(logRepository));
            _loginRepository = loginRepository ?? throw new ArgumentNullException(nameof(loginRepository));
            _alertRepository = alertRepository;
            _options = options ?? new SecurityLogOptions();
        }

        /// <inheritdoc />
        public void Log(SecurityLogEntry entry)
        {
            if (entry == null) return;

            // Mask sensitive data
            if (_options.MaskIpAddress && !string.IsNullOrEmpty(entry.IpAddress))
            {
                entry.IpAddressHash = HashIpAddress(entry.IpAddress);
                entry.IpAddress = MaskIpAddress(entry.IpAddress);
            }

            if (_options.MaskUsername && !string.IsNullOrEmpty(entry.Username))
            {
                entry.Username = MaskUsername(entry.Username);
            }

            // Set timestamp if not set
            if (entry.Timestamp == default)
                entry.Timestamp = DateTime.UtcNow;

            // Add to repository
            _logRepository.AddLog(entry);

            // Check alerts
            CheckAlerts(entry);
        }

        /// <inheritdoc />
        public void Log(Action<SecurityLogEntryBuilder> configure)
        {
            var builder = new SecurityLogEntryBuilder();
            configure(builder);
            Log(builder.Build());
        }

        /// <inheritdoc />
        public void LogLogin(LoginRecord record)
        {
            if (record == null) return;

            // Check if new device
            if (record.UserId.HasValue && !string.IsNullOrEmpty(record.Fingerprint))
            {
                record.IsNewDevice = _loginRepository.IsNewDevice(record.UserId.Value, record.Fingerprint);
            }

            // Mask data
            if (_options.MaskIpAddress && !string.IsNullOrEmpty(record.IpAddress))
            {
                record.IpAddressHash = HashIpAddress(record.IpAddress);
                record.IpAddress = MaskIpAddress(record.IpAddress);
            }

            if (_options.MaskUsername && !string.IsNullOrEmpty(record.Username))
            {
                record.Username = MaskUsername(record.Username);
            }

            if (record.Timestamp == default)
                record.Timestamp = DateTime.UtcNow;

            _loginRepository.AddLoginRecord(record);

            // Also add to security log
            var entry = new SecurityLogEntry
            {
                Timestamp = record.Timestamp,
                EventType = record.Success ? SecurityEventType.LoginSuccess : SecurityEventType.LoginFailed,
                Severity = record.Success ? SecuritySeverity.Info : SecuritySeverity.Low,
                Outcome = record.Success ? EventOutcome.Success : EventOutcome.Failure,
                UserId = record.UserId,
                Username = record.Username,
                IpAddress = record.IpAddress,
                IpAddressHash = record.IpAddressHash,
                UserAgent = record.UserAgent,
                Fingerprint = record.Fingerprint,
                SessionId = record.SessionId,
                Message = record.Success ? "User logged in successfully" : $"Login failed: {record.FailureReason}",
                Browser = record.Browser,
                OperatingSystem = record.OperatingSystem,
                DeviceType = record.DeviceType,
                Location = record.Location,
                CountryCode = record.CountryCode
            };

            if (record.IsSuspicious)
            {
                entry.Severity = SecuritySeverity.High;
                entry.Tags.Add("suspicious");
            }

            if (record.IsNewDevice)
            {
                entry.Tags.Add("new_device");
            }

            _logRepository.AddLog(entry);
        }

        /// <inheritdoc />
        public void LogLoginSuccess(int userId, string username, string ipAddress, string userAgent,
            string sessionId = null, bool mfaVerified = false, string mfaMethod = null)
        {
            var record = new LoginRecord
            {
                UserId = userId,
                Username = username,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Success = true,
                SessionId = sessionId,
                MfaVerified = mfaVerified,
                MfaMethod = mfaMethod
            };

            ParseUserAgent(record, userAgent);
            LogLogin(record);
        }

        /// <inheritdoc />
        public void LogLoginFailed(string username, string ipAddress, string userAgent, string reason)
        {
            var record = new LoginRecord
            {
                Username = username,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Success = false,
                FailureReason = reason
            };

            ParseUserAgent(record, userAgent);

            // Check for suspicious patterns
            var recentFailed = _loginRepository.GetRecentFailedLoginsByIp(HashIpAddress(ipAddress), 10);
            if (recentFailed.Count >= 5)
            {
                record.IsSuspicious = true;
                record.SuspiciousReason = "Multiple failed login attempts from this IP";
            }

            LogLogin(record);
        }

        /// <inheritdoc />
        public void LogLogout(int userId, string username, string ipAddress, string sessionId = null)
        {
            Log(b => b
                .WithEventType(SecurityEventType.LogoutSuccess)
                .WithSeverity(SecuritySeverity.Info)
                .WithOutcome(EventOutcome.Success)
                .WithUser(userId, username)
                .WithIp(ipAddress)
                .WithSessionId(sessionId)
                .WithMessage("User logged out"));
        }

        /// <inheritdoc />
        public void LogMfaEvent(SecurityEventType eventType, int userId, string username, string ipAddress,
            bool success, string method = null, string details = null)
        {
            var severity = eventType switch
            {
                SecurityEventType.MfaVerifyFailed => SecuritySeverity.Medium,
                SecurityEventType.MfaRecoveryCodeUsed => SecuritySeverity.Medium,
                SecurityEventType.MfaDisabled => SecuritySeverity.Medium,
                _ => SecuritySeverity.Info
            };

            Log(b => b
                .WithEventType(eventType)
                .WithSeverity(severity)
                .WithOutcome(success ? EventOutcome.Success : EventOutcome.Failure)
                .WithUser(userId, username)
                .WithIp(ipAddress)
                .WithMessage(GetMfaEventMessage(eventType, success, method))
                .WithDetails(details)
                .WithTag("mfa"));
        }

        /// <inheritdoc />
        public void LogAccountEvent(SecurityEventType eventType, int userId, string username, string ipAddress,
            string details = null)
        {
            var severity = eventType switch
            {
                SecurityEventType.AccountLocked => SecuritySeverity.High,
                SecurityEventType.AccountDeleted => SecuritySeverity.High,
                SecurityEventType.PasswordChanged => SecuritySeverity.Medium,
                SecurityEventType.EmailChanged => SecuritySeverity.Medium,
                _ => SecuritySeverity.Info
            };

            Log(b => b
                .WithEventType(eventType)
                .WithSeverity(severity)
                .WithOutcome(EventOutcome.Success)
                .WithUser(userId, username)
                .WithIp(ipAddress)
                .WithMessage(GetAccountEventMessage(eventType))
                .WithDetails(details)
                .WithTag("account"));
        }

        /// <inheritdoc />
        public void LogSecurityEvent(SecurityEventType eventType, string ipAddress, string userAgent,
            string message, SecuritySeverity severity = SecuritySeverity.Medium, string details = null)
        {
            Log(b => b
                .WithEventType(eventType)
                .WithSeverity(severity)
                .WithOutcome(EventOutcome.Unknown)
                .WithIp(ipAddress)
                .WithUserAgent(userAgent)
                .WithMessage(message)
                .WithDetails(details)
                .WithTag("security"));
        }

        /// <inheritdoc />
        public void LogAdminAction(int adminUserId, string adminUsername, string action, string targetResource,
            string ipAddress, string details = null)
        {
            Log(b => b
                .WithEventType(SecurityEventType.AdminAction)
                .WithSeverity(SecuritySeverity.Medium)
                .WithOutcome(EventOutcome.Success)
                .WithUser(adminUserId, adminUsername)
                .WithIp(ipAddress)
                .WithResource(targetResource)
                .WithMessage($"Admin action: {action}")
                .WithDetails(details)
                .WithTag("admin"));
        }

        /// <inheritdoc />
        public SecurityLogResult Query(SecurityLogQuery query)
        {
            return _logRepository.Query(query);
        }

        /// <inheritdoc />
        public SecurityLogEntry GetById(long id)
        {
            return _logRepository.GetById(id);
        }

        /// <inheritdoc />
        public LoginHistoryResult GetUserLoginHistory(int userId, int page = 1, int pageSize = 20)
        {
            return _loginRepository.GetUserLoginHistory(userId, page, pageSize);
        }

        /// <inheritdoc />
        public LoginHistoryResult GetIpLoginHistory(string ipAddress, int page = 1, int pageSize = 20)
        {
            var hash = HashIpAddress(ipAddress);
            return _loginRepository.GetIpLoginHistory(hash, page, pageSize);
        }

        /// <inheritdoc />
        public List<LoginRecord> GetRecentFailedLogins(int userId, int count = 10)
        {
            return _loginRepository.GetRecentFailedLogins(userId, count);
        }

        /// <inheritdoc />
        public List<LoginRecord> GetRecentFailedLoginsByIp(string ipAddress, int count = 10)
        {
            var hash = HashIpAddress(ipAddress);
            return _loginRepository.GetRecentFailedLoginsByIp(hash, count);
        }

        /// <inheritdoc />
        public List<LoginRecord> GetSuspiciousLogins(DateTime since, int count = 100)
        {
            return _loginRepository.GetSuspiciousLogins(since, count);
        }

        /// <inheritdoc />
        public UserActivitySummary GetUserActivity(int userId, DateTime? since = null)
        {
            return _logRepository.GetUserActivity(userId, since);
        }

        /// <inheritdoc />
        public IpActivitySummary GetIpActivity(string ipAddress, DateTime? since = null)
        {
            var hash = HashIpAddress(ipAddress);
            return _logRepository.GetIpActivity(hash, since);
        }

        /// <inheritdoc />
        public SecurityStatistics GetStatistics(DateTime startDate, DateTime endDate)
        {
            return _logRepository.GetStatistics(startDate, endDate);
        }

        /// <inheritdoc />
        public SecurityStatistics GetStatistics(int days = 7)
        {
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-days);
            return GetStatistics(startDate, endDate);
        }

        /// <inheritdoc />
        public bool IsNewDevice(int userId, string fingerprint)
        {
            return _loginRepository.IsNewDevice(userId, fingerprint);
        }

        /// <inheritdoc />
        public int DeleteUserLogs(int userId)
        {
            // Note: This is for GDPR compliance
            return _logRepository.DeleteByUserId(userId);
        }

        /// <inheritdoc />
        public List<SecurityAlert> GetUnacknowledgedAlerts()
        {
            return _alertRepository?.GetUnacknowledgedAlerts() ?? new List<SecurityAlert>();
        }

        /// <inheritdoc />
        public void AcknowledgeAlert(long alertId, int userId, string note)
        {
            _alertRepository?.AcknowledgeAlert(alertId, userId, note);
        }

        #region Private Methods

        private static string HashIpAddress(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return null;

            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(ipAddress.ToLowerInvariant());
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private static string MaskIpAddress(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return ipAddress;

            // IPv4: mask last octet
            if (ipAddress.Contains('.') && !ipAddress.Contains(':'))
            {
                var parts = ipAddress.Split('.');
                if (parts.Length == 4)
                {
                    return $"{parts[0]}.{parts[1]}.{parts[2]}.***";
                }
            }

            // IPv6: mask last 4 segments
            if (ipAddress.Contains(':'))
            {
                var parts = ipAddress.Split(':');
                if (parts.Length >= 4)
                {
                    return string.Join(":", parts[..4]) + ":****:****:****:****";
                }
            }

            return ipAddress;
        }

        private static string MaskUsername(string username)
        {
            if (string.IsNullOrEmpty(username))
                return username;

            // Email: mask middle of local part
            if (username.Contains('@'))
            {
                var parts = username.Split('@');
                var local = parts[0];
                if (local.Length > 2)
                {
                    var masked = local[0] + new string('*', Math.Min(local.Length - 2, 5)) + local[^1];
                    return masked + "@" + parts[1];
                }
            }

            // Regular username: show first and last char
            if (username.Length > 2)
            {
                return username[0] + new string('*', Math.Min(username.Length - 2, 5)) + username[^1];
            }

            return username;
        }

        private static void ParseUserAgent(LoginRecord record, string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
                return;

            // Simple UA parsing - in production, use a proper library
            var ua = userAgent.ToLower();

            // Browser detection
            if (ua.Contains("edg/"))
                record.Browser = "Edge";
            else if (ua.Contains("chrome/"))
                record.Browser = "Chrome";
            else if (ua.Contains("firefox/"))
                record.Browser = "Firefox";
            else if (ua.Contains("safari/") && !ua.Contains("chrome"))
                record.Browser = "Safari";
            else if (ua.Contains("msie") || ua.Contains("trident"))
                record.Browser = "Internet Explorer";

            // OS detection
            if (ua.Contains("windows"))
                record.OperatingSystem = "Windows";
            else if (ua.Contains("mac os"))
                record.OperatingSystem = "macOS";
            else if (ua.Contains("linux"))
                record.OperatingSystem = "Linux";
            else if (ua.Contains("android"))
                record.OperatingSystem = "Android";
            else if (ua.Contains("iphone") || ua.Contains("ipad"))
                record.OperatingSystem = "iOS";

            // Device type
            if (ua.Contains("mobile") || ua.Contains("android") || ua.Contains("iphone"))
                record.DeviceType = "Mobile";
            else if (ua.Contains("tablet") || ua.Contains("ipad"))
                record.DeviceType = "Tablet";
            else
                record.DeviceType = "Desktop";
        }

        private static string GetMfaEventMessage(SecurityEventType eventType, bool success, string method)
        {
            return eventType switch
            {
                SecurityEventType.MfaEnabled => $"MFA enabled using {method ?? "unknown"} method",
                SecurityEventType.MfaDisabled => "MFA disabled",
                SecurityEventType.MfaVerifySuccess => $"MFA verification successful ({method ?? "unknown"})",
                SecurityEventType.MfaVerifyFailed => $"MFA verification failed ({method ?? "unknown"})",
                SecurityEventType.MfaRecoveryCodeUsed => "Recovery code used for MFA",
                SecurityEventType.MfaRecoveryCodesRegenerated => "MFA recovery codes regenerated",
                _ => "MFA event"
            };
        }

        private static string GetAccountEventMessage(SecurityEventType eventType)
        {
            return eventType switch
            {
                SecurityEventType.AccountCreated => "Account created",
                SecurityEventType.AccountUpdated => "Account updated",
                SecurityEventType.AccountDeleted => "Account deleted",
                SecurityEventType.AccountLocked => "Account locked",
                SecurityEventType.AccountUnlocked => "Account unlocked",
                SecurityEventType.PasswordChanged => "Password changed",
                SecurityEventType.PasswordResetRequested => "Password reset requested",
                SecurityEventType.PasswordResetCompleted => "Password reset completed",
                SecurityEventType.EmailChanged => "Email changed",
                SecurityEventType.EmailVerified => "Email verified",
                _ => "Account event"
            };
        }

        private void CheckAlerts(SecurityLogEntry entry)
        {
            if (_alertRepository == null)
                return;

            // Get enabled alert configs
            var configs = _alertRepository.GetAlertConfigs(enabledOnly: true);

            foreach (var config in configs)
            {
                if (config.EventType != entry.EventType)
                    continue;

                if (entry.Severity < config.MinSeverity)
                    continue;

                // Check threshold
                var since = DateTime.UtcNow.AddMinutes(-config.ThresholdMinutes);
                var query = new SecurityLogQuery
                {
                    StartDate = since,
                    EventTypes = new List<SecurityEventType> { config.EventType },
                    Severities = new List<SecuritySeverity> { config.MinSeverity, SecuritySeverity.High, SecuritySeverity.Critical }
                };

                var result = _logRepository.Query(query);
                if (result.TotalCount >= config.ThresholdCount)
                {
                    // Trigger alert
                    var alert = new SecurityAlert
                    {
                        ConfigId = config.Id,
                        AlertName = config.Name,
                        Severity = entry.Severity,
                        Message = $"Alert: {config.Description}",
                        Details = JsonSerializer.Serialize(new
                        {
                            EventCount = result.TotalCount,
                            ThresholdMinutes = config.ThresholdMinutes,
                            TriggeringEvent = entry.Message
                        }),
                        RelatedLogIds = new List<long> { entry.Id }
                    };

                    _alertRepository.AddAlert(alert);

                    // Update last triggered
                    config.LastTriggered = DateTime.UtcNow;
                    _alertRepository.UpdateAlertConfig(config);
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Security log options
    /// </summary>
    public class SecurityLogOptions
    {
        /// <summary>
        /// Whether to mask IP addresses in logs
        /// </summary>
        public bool MaskIpAddress { get; set; } = true;

        /// <summary>
        /// Whether to mask usernames in logs
        /// </summary>
        public bool MaskUsername { get; set; } = true;

        /// <summary>
        /// Retention period in days
        /// </summary>
        public int RetentionDays { get; set; } = 90;

        /// <summary>
        /// Maximum log entries to keep
        /// </summary>
        public int MaxEntries { get; set; } = 100000;
    }

    /// <summary>
    /// Fluent builder for security log entries
    /// </summary>
    public class SecurityLogEntryBuilder
    {
        private readonly SecurityLogEntry _entry = new();

        public SecurityLogEntryBuilder WithEventType(SecurityEventType eventType)
        {
            _entry.EventType = eventType;
            return this;
        }

        public SecurityLogEntryBuilder WithSeverity(SecuritySeverity severity)
        {
            _entry.Severity = severity;
            return this;
        }

        public SecurityLogEntryBuilder WithOutcome(EventOutcome outcome)
        {
            _entry.Outcome = outcome;
            return this;
        }

        public SecurityLogEntryBuilder WithUser(int userId, string username = null)
        {
            _entry.UserId = userId;
            _entry.Username = username;
            return this;
        }

        public SecurityLogEntryBuilder WithIp(string ipAddress)
        {
            _entry.IpAddress = ipAddress;
            return this;
        }

        public SecurityLogEntryBuilder WithUserAgent(string userAgent)
        {
            _entry.UserAgent = userAgent;
            return this;
        }

        public SecurityLogEntryBuilder WithFingerprint(string fingerprint)
        {
            _entry.Fingerprint = fingerprint;
            return this;
        }

        public SecurityLogEntryBuilder WithSessionId(string sessionId)
        {
            _entry.SessionId = sessionId;
            return this;
        }

        public SecurityLogEntryBuilder WithRequestId(string requestId)
        {
            _entry.RequestId = requestId;
            return this;
        }

        public SecurityLogEntryBuilder WithCorrelationId(string correlationId)
        {
            _entry.CorrelationId = correlationId;
            return this;
        }

        public SecurityLogEntryBuilder WithMessage(string message)
        {
            _entry.Message = message;
            return this;
        }

        public SecurityLogEntryBuilder WithDetails(string details)
        {
            _entry.Details = details;
            return this;
        }

        public SecurityLogEntryBuilder WithDetails(object details)
        {
            _entry.Details = details != null ? JsonSerializer.Serialize(details) : null;
            return this;
        }

        public SecurityLogEntryBuilder WithResource(string resource)
        {
            _entry.Resource = resource;
            return this;
        }

        public SecurityLogEntryBuilder WithHttpInfo(string method, string path, int? statusCode = null)
        {
            _entry.HttpMethod = method;
            _entry.RequestPath = path;
            _entry.StatusCode = statusCode;
            return this;
        }

        public SecurityLogEntryBuilder WithDuration(long durationMs)
        {
            _entry.DurationMs = durationMs;
            return this;
        }

        public SecurityLogEntryBuilder WithLocation(string location, string countryCode = null)
        {
            _entry.Location = location;
            _entry.CountryCode = countryCode;
            return this;
        }

        public SecurityLogEntryBuilder WithDeviceInfo(string browser, string os, string deviceType, bool isBot = false)
        {
            _entry.Browser = browser;
            _entry.OperatingSystem = os;
            _entry.DeviceType = deviceType;
            _entry.IsBot = isBot;
            return this;
        }

        public SecurityLogEntryBuilder WithTag(string tag)
        {
            _entry.Tags ??= new List<string>();
            if (!_entry.Tags.Contains(tag))
                _entry.Tags.Add(tag);
            return this;
        }

        public SecurityLogEntryBuilder WithTags(params string[] tags)
        {
            _entry.Tags ??= new List<string>();
            foreach (var tag in tags)
            {
                if (!_entry.Tags.Contains(tag))
                    _entry.Tags.Add(tag);
            }
            return this;
        }

        public SecurityLogEntry Build()
        {
            if (_entry.Timestamp == default)
                _entry.Timestamp = DateTime.UtcNow;
            return _entry;
        }
    }
}
