using System;
using System.Collections.Generic;

namespace Bricks4Agent.Security.AuditLog.Models
{
    /// <summary>
    /// Security event types
    /// </summary>
    public enum SecurityEventType
    {
        // Authentication events (1xx)
        LoginSuccess = 100,
        LoginFailed = 101,
        LogoutSuccess = 102,
        SessionExpired = 103,
        SessionInvalidated = 104,

        // MFA events (2xx)
        MfaEnabled = 200,
        MfaDisabled = 201,
        MfaVerifySuccess = 202,
        MfaVerifyFailed = 203,
        MfaRecoveryCodeUsed = 204,
        MfaRecoveryCodesRegenerated = 205,

        // Account events (3xx)
        AccountCreated = 300,
        AccountUpdated = 301,
        AccountDeleted = 302,
        AccountLocked = 303,
        AccountUnlocked = 304,
        PasswordChanged = 305,
        PasswordResetRequested = 306,
        PasswordResetCompleted = 307,
        EmailChanged = 308,
        EmailVerified = 309,

        // Security events (4xx)
        RateLimitExceeded = 400,
        SuspiciousActivity = 401,
        IpBlocked = 402,
        IpUnblocked = 403,
        BruteForceDetected = 404,
        UnauthorizedAccess = 405,
        TokenRevoked = 406,
        InvalidToken = 407,

        // Admin events (5xx)
        AdminLogin = 500,
        AdminAction = 501,
        PermissionChanged = 502,
        RoleAssigned = 503,
        RoleRevoked = 504,
        SystemConfigChanged = 505,

        // Data access events (6xx)
        SensitiveDataAccessed = 600,
        DataExported = 601,
        BulkOperation = 602,

        // Other (9xx)
        Custom = 900,
        Unknown = 999
    }

    /// <summary>
    /// Security event severity levels
    /// </summary>
    public enum SecuritySeverity
    {
        /// <summary>
        /// Informational - normal operations
        /// </summary>
        Info = 0,

        /// <summary>
        /// Low - minor security events
        /// </summary>
        Low = 1,

        /// <summary>
        /// Medium - notable security events
        /// </summary>
        Medium = 2,

        /// <summary>
        /// High - significant security events requiring attention
        /// </summary>
        High = 3,

        /// <summary>
        /// Critical - immediate attention required
        /// </summary>
        Critical = 4
    }

    /// <summary>
    /// Security event outcome
    /// </summary>
    public enum EventOutcome
    {
        Success = 0,
        Failure = 1,
        Unknown = 2
    }

    /// <summary>
    /// Security log entry
    /// </summary>
    public class SecurityLogEntry
    {
        /// <summary>
        /// Unique log entry ID
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Event timestamp (UTC)
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Event type
        /// </summary>
        public SecurityEventType EventType { get; set; }

        /// <summary>
        /// Event severity
        /// </summary>
        public SecuritySeverity Severity { get; set; }

        /// <summary>
        /// Event outcome
        /// </summary>
        public EventOutcome Outcome { get; set; }

        /// <summary>
        /// User ID (if applicable)
        /// </summary>
        public int? UserId { get; set; }

        /// <summary>
        /// Username or email (for display, partially masked)
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Client IP address (partially masked for privacy)
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// Full IP address hash (for searching)
        /// </summary>
        public string IpAddressHash { get; set; }

        /// <summary>
        /// User agent string
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        /// Device fingerprint
        /// </summary>
        public string Fingerprint { get; set; }

        /// <summary>
        /// Session ID (if applicable)
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Request ID for correlation
        /// </summary>
        public string RequestId { get; set; }

        /// <summary>
        /// Event description
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Additional details (JSON format)
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// Target resource (e.g., endpoint, entity)
        /// </summary>
        public string Resource { get; set; }

        /// <summary>
        /// HTTP method (if applicable)
        /// </summary>
        public string HttpMethod { get; set; }

        /// <summary>
        /// Request path
        /// </summary>
        public string RequestPath { get; set; }

        /// <summary>
        /// Response status code
        /// </summary>
        public int? StatusCode { get; set; }

        /// <summary>
        /// Duration in milliseconds
        /// </summary>
        public long? DurationMs { get; set; }

        /// <summary>
        /// Geographic location (if available)
        /// </summary>
        public string Location { get; set; }

        /// <summary>
        /// Country code (if available)
        /// </summary>
        public string CountryCode { get; set; }

        /// <summary>
        /// Browser info
        /// </summary>
        public string Browser { get; set; }

        /// <summary>
        /// Operating system
        /// </summary>
        public string OperatingSystem { get; set; }

        /// <summary>
        /// Device type
        /// </summary>
        public string DeviceType { get; set; }

        /// <summary>
        /// Whether this is from a known bot
        /// </summary>
        public bool IsBot { get; set; }

        /// <summary>
        /// Related log entries (e.g., for tracking attack patterns)
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// Tags for categorization
        /// </summary>
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Login record with detailed information
    /// </summary>
    public class LoginRecord
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public int? UserId { get; set; }
        public string Username { get; set; }
        public string IpAddress { get; set; }
        public string IpAddressHash { get; set; }
        public bool Success { get; set; }
        public string FailureReason { get; set; }
        public string UserAgent { get; set; }
        public string Browser { get; set; }
        public string OperatingSystem { get; set; }
        public string DeviceType { get; set; }
        public string Location { get; set; }
        public string CountryCode { get; set; }
        public string SessionId { get; set; }
        public bool MfaRequired { get; set; }
        public bool MfaVerified { get; set; }
        public string MfaMethod { get; set; }
        public string Fingerprint { get; set; }
        public bool IsNewDevice { get; set; }
        public bool IsSuspicious { get; set; }
        public string SuspiciousReason { get; set; }
    }

    /// <summary>
    /// Log query parameters
    /// </summary>
    public class SecurityLogQuery
    {
        /// <summary>
        /// Start date filter
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// End date filter
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Filter by event types
        /// </summary>
        public List<SecurityEventType> EventTypes { get; set; }

        /// <summary>
        /// Filter by severity levels
        /// </summary>
        public List<SecuritySeverity> Severities { get; set; }

        /// <summary>
        /// Filter by outcome
        /// </summary>
        public EventOutcome? Outcome { get; set; }

        /// <summary>
        /// Filter by user ID
        /// </summary>
        public int? UserId { get; set; }

        /// <summary>
        /// Filter by username (partial match)
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Filter by IP address hash
        /// </summary>
        public string IpAddressHash { get; set; }

        /// <summary>
        /// Filter by session ID
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Filter by correlation ID
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// Filter by request ID
        /// </summary>
        public string RequestId { get; set; }

        /// <summary>
        /// Search in message
        /// </summary>
        public string SearchText { get; set; }

        /// <summary>
        /// Filter by tags
        /// </summary>
        public List<string> Tags { get; set; }

        /// <summary>
        /// Filter by country code
        /// </summary>
        public string CountryCode { get; set; }

        /// <summary>
        /// Only suspicious events
        /// </summary>
        public bool? SuspiciousOnly { get; set; }

        /// <summary>
        /// Page number (1-based)
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// Page size
        /// </summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// Sort field
        /// </summary>
        public string SortBy { get; set; } = "Timestamp";

        /// <summary>
        /// Sort descending
        /// </summary>
        public bool SortDescending { get; set; } = true;
    }

    /// <summary>
    /// Paged log result
    /// </summary>
    public class SecurityLogResult
    {
        public List<SecurityLogEntry> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasNextPage => Page < TotalPages;
        public bool HasPreviousPage => Page > 1;
    }

    /// <summary>
    /// Login history result
    /// </summary>
    public class LoginHistoryResult
    {
        public List<LoginRecord> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    /// <summary>
    /// Security statistics
    /// </summary>
    public class SecurityStatistics
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        // Login stats
        public int TotalLoginAttempts { get; set; }
        public int SuccessfulLogins { get; set; }
        public int FailedLogins { get; set; }
        public double LoginSuccessRate => TotalLoginAttempts > 0
            ? (double)SuccessfulLogins / TotalLoginAttempts * 100 : 0;

        // MFA stats
        public int MfaVerifications { get; set; }
        public int MfaFailures { get; set; }
        public int RecoveryCodesUsed { get; set; }

        // Security events
        public int RateLimitExceeded { get; set; }
        public int SuspiciousActivities { get; set; }
        public int BlockedIps { get; set; }
        public int BruteForceAttempts { get; set; }

        // Account events
        public int NewAccounts { get; set; }
        public int AccountsLocked { get; set; }
        public int PasswordChanges { get; set; }

        // Breakdown by severity
        public Dictionary<SecuritySeverity, int> BySeverity { get; set; } = new();

        // Breakdown by event type
        public Dictionary<SecurityEventType, int> ByEventType { get; set; } = new();

        // Top IPs by activity
        public List<IpActivitySummary> TopActiveIps { get; set; } = new();

        // Top failed login IPs
        public List<IpActivitySummary> TopFailedLoginIps { get; set; } = new();

        // Login by country
        public Dictionary<string, int> ByCountry { get; set; } = new();

        // Login by hour of day
        public Dictionary<int, int> ByHourOfDay { get; set; } = new();

        // Login by device type
        public Dictionary<string, int> ByDeviceType { get; set; } = new();
    }

    /// <summary>
    /// IP activity summary
    /// </summary>
    public class IpActivitySummary
    {
        public string IpAddressMasked { get; set; }
        public string IpAddressHash { get; set; }
        public int TotalRequests { get; set; }
        public int FailedAttempts { get; set; }
        public int SuccessfulAttempts { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsBlocked { get; set; }
        public bool IsSuspicious { get; set; }
        public List<string> Countries { get; set; } = new();
    }

    /// <summary>
    /// User activity summary
    /// </summary>
    public class UserActivitySummary
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public int TotalLogins { get; set; }
        public int FailedLogins { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime? LastFailedLogin { get; set; }
        public int ActiveSessions { get; set; }
        public int UniqueIps { get; set; }
        public int UniqueDevices { get; set; }
        public bool MfaEnabled { get; set; }
        public List<string> RecentIps { get; set; } = new();
        public List<string> RecentCountries { get; set; } = new();
    }

    /// <summary>
    /// Alert configuration
    /// </summary>
    public class SecurityAlertConfig
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public SecurityEventType EventType { get; set; }
        public SecuritySeverity MinSeverity { get; set; }
        public int ThresholdCount { get; set; }
        public int ThresholdMinutes { get; set; }
        public bool IsEnabled { get; set; }
        public string NotificationChannels { get; set; } // JSON: ["email", "slack", "webhook"]
        public string Recipients { get; set; } // JSON array of emails/endpoints
        public DateTime CreatedAt { get; set; }
        public DateTime? LastTriggered { get; set; }
    }

    /// <summary>
    /// Security alert
    /// </summary>
    public class SecurityAlert
    {
        public long Id { get; set; }
        public int ConfigId { get; set; }
        public string AlertName { get; set; }
        public SecuritySeverity Severity { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public DateTime TriggeredAt { get; set; }
        public bool IsAcknowledged { get; set; }
        public int? AcknowledgedByUserId { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public string AcknowledgementNote { get; set; }
        public List<long> RelatedLogIds { get; set; } = new();
    }
}
