using System;
using System.Collections.Generic;
using Bricks4Agent.Security.AuditLog.Models;

namespace Bricks4Agent.Security.AuditLog
{
    /// <summary>
    /// Security log repository interface
    /// </summary>
    public interface ISecurityLogRepository
    {
        /// <summary>
        /// Add a new log entry
        /// </summary>
        long AddLog(SecurityLogEntry entry);

        /// <summary>
        /// Add multiple log entries
        /// </summary>
        void AddLogs(IEnumerable<SecurityLogEntry> entries);

        /// <summary>
        /// Get log entry by ID
        /// </summary>
        SecurityLogEntry GetById(long id);

        /// <summary>
        /// Query logs with filters
        /// </summary>
        SecurityLogResult Query(SecurityLogQuery query);

        /// <summary>
        /// Get logs by correlation ID
        /// </summary>
        List<SecurityLogEntry> GetByCorrelationId(string correlationId);

        /// <summary>
        /// Get logs by request ID
        /// </summary>
        List<SecurityLogEntry> GetByRequestId(string requestId);

        /// <summary>
        /// Get recent logs for a user
        /// </summary>
        List<SecurityLogEntry> GetUserLogs(int userId, int count = 100);

        /// <summary>
        /// Get recent logs for an IP address
        /// </summary>
        List<SecurityLogEntry> GetIpLogs(string ipAddressHash, int count = 100);

        /// <summary>
        /// Get statistics for a time period
        /// </summary>
        SecurityStatistics GetStatistics(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Get user activity summary
        /// </summary>
        UserActivitySummary GetUserActivity(int userId, DateTime? since = null);

        /// <summary>
        /// Get IP activity summary
        /// </summary>
        IpActivitySummary GetIpActivity(string ipAddressHash, DateTime? since = null);

        /// <summary>
        /// Delete logs older than specified date
        /// </summary>
        int DeleteOlderThan(DateTime cutoff);

        /// <summary>
        /// Delete logs by user ID (for GDPR compliance)
        /// </summary>
        int DeleteByUserId(int userId);

        /// <summary>
        /// Get log count
        /// </summary>
        long GetTotalCount();

        /// <summary>
        /// Get log count by event type
        /// </summary>
        Dictionary<SecurityEventType, int> GetCountByEventType(DateTime? since = null);
    }

    /// <summary>
    /// Login record repository interface
    /// </summary>
    public interface ILoginRecordRepository
    {
        /// <summary>
        /// Add a login record
        /// </summary>
        long AddLoginRecord(LoginRecord record);

        /// <summary>
        /// Get login record by ID
        /// </summary>
        LoginRecord GetById(long id);

        /// <summary>
        /// Get login history for a user
        /// </summary>
        LoginHistoryResult GetUserLoginHistory(int userId, int page = 1, int pageSize = 20);

        /// <summary>
        /// Get login history for an IP
        /// </summary>
        LoginHistoryResult GetIpLoginHistory(string ipAddressHash, int page = 1, int pageSize = 20);

        /// <summary>
        /// Get recent failed logins for a user
        /// </summary>
        List<LoginRecord> GetRecentFailedLogins(int userId, int count = 10);

        /// <summary>
        /// Get recent failed logins for an IP
        /// </summary>
        List<LoginRecord> GetRecentFailedLoginsByIp(string ipAddressHash, int count = 10);

        /// <summary>
        /// Get recent successful logins for a user
        /// </summary>
        List<LoginRecord> GetRecentSuccessfulLogins(int userId, int count = 10);

        /// <summary>
        /// Get suspicious login records
        /// </summary>
        List<LoginRecord> GetSuspiciousLogins(DateTime since, int count = 100);

        /// <summary>
        /// Check if this is a new device for user
        /// </summary>
        bool IsNewDevice(int userId, string fingerprint);

        /// <summary>
        /// Get unique devices for user
        /// </summary>
        List<string> GetUserDevices(int userId);

        /// <summary>
        /// Delete login records older than specified date
        /// </summary>
        int DeleteOlderThan(DateTime cutoff);
    }

    /// <summary>
    /// Security alert repository interface
    /// </summary>
    public interface ISecurityAlertRepository
    {
        /// <summary>
        /// Add alert configuration
        /// </summary>
        int AddAlertConfig(SecurityAlertConfig config);

        /// <summary>
        /// Update alert configuration
        /// </summary>
        void UpdateAlertConfig(SecurityAlertConfig config);

        /// <summary>
        /// Get alert configuration by ID
        /// </summary>
        SecurityAlertConfig GetAlertConfig(int id);

        /// <summary>
        /// Get all alert configurations
        /// </summary>
        List<SecurityAlertConfig> GetAlertConfigs(bool enabledOnly = false);

        /// <summary>
        /// Delete alert configuration
        /// </summary>
        void DeleteAlertConfig(int id);

        /// <summary>
        /// Add security alert
        /// </summary>
        long AddAlert(SecurityAlert alert);

        /// <summary>
        /// Get alert by ID
        /// </summary>
        SecurityAlert GetAlert(long id);

        /// <summary>
        /// Get unacknowledged alerts
        /// </summary>
        List<SecurityAlert> GetUnacknowledgedAlerts();

        /// <summary>
        /// Get recent alerts
        /// </summary>
        List<SecurityAlert> GetRecentAlerts(int count = 50);

        /// <summary>
        /// Acknowledge alert
        /// </summary>
        void AcknowledgeAlert(long alertId, int userId, string note);

        /// <summary>
        /// Delete old alerts
        /// </summary>
        int DeleteOlderThan(DateTime cutoff);
    }
}
