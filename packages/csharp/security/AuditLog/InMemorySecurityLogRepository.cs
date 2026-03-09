using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bricks4Agent.Security.AuditLog.Models;

namespace Bricks4Agent.Security.AuditLog
{
    /// <summary>
    /// In-memory implementation of security log repository
    /// For production, use a database-backed implementation
    /// </summary>
    public class InMemorySecurityLogRepository : ISecurityLogRepository, IDisposable
    {
        private readonly ConcurrentDictionary<long, SecurityLogEntry> _logs = new();
        private readonly ConcurrentDictionary<int, List<long>> _userLogIndex = new();
        private readonly ConcurrentDictionary<string, List<long>> _ipLogIndex = new();
        private readonly ConcurrentDictionary<string, List<long>> _correlationIndex = new();
        private long _idCounter = 0;
        private readonly object _indexLock = new();
        private readonly Timer _cleanupTimer;
        private readonly int _maxEntries;
        private readonly int _retentionDays;
        private bool _disposed;

        public InMemorySecurityLogRepository(int maxEntries = 100000, int retentionDays = 90)
        {
            _maxEntries = maxEntries;
            _retentionDays = retentionDays;
            _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }

        /// <inheritdoc />
        public long AddLog(SecurityLogEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            entry.Id = Interlocked.Increment(ref _idCounter);
            if (entry.Timestamp == default)
                entry.Timestamp = DateTime.UtcNow;

            _logs[entry.Id] = entry;

            // Update indexes
            lock (_indexLock)
            {
                if (entry.UserId.HasValue)
                {
                    if (!_userLogIndex.TryGetValue(entry.UserId.Value, out var userLogs))
                    {
                        userLogs = new List<long>();
                        _userLogIndex[entry.UserId.Value] = userLogs;
                    }
                    userLogs.Add(entry.Id);
                }

                if (!string.IsNullOrEmpty(entry.IpAddressHash))
                {
                    if (!_ipLogIndex.TryGetValue(entry.IpAddressHash, out var ipLogs))
                    {
                        ipLogs = new List<long>();
                        _ipLogIndex[entry.IpAddressHash] = ipLogs;
                    }
                    ipLogs.Add(entry.Id);
                }

                if (!string.IsNullOrEmpty(entry.CorrelationId))
                {
                    if (!_correlationIndex.TryGetValue(entry.CorrelationId, out var corrLogs))
                    {
                        corrLogs = new List<long>();
                        _correlationIndex[entry.CorrelationId] = corrLogs;
                    }
                    corrLogs.Add(entry.Id);
                }
            }

            // Enforce max entries
            EnforceMaxEntries();

            return entry.Id;
        }

        /// <inheritdoc />
        public void AddLogs(IEnumerable<SecurityLogEntry> entries)
        {
            foreach (var entry in entries)
            {
                AddLog(entry);
            }
        }

        /// <inheritdoc />
        public SecurityLogEntry GetById(long id)
        {
            _logs.TryGetValue(id, out var entry);
            return entry;
        }

        /// <inheritdoc />
        public SecurityLogResult Query(SecurityLogQuery query)
        {
            query ??= new SecurityLogQuery();

            var items = _logs.Values.AsEnumerable();

            // Apply filters
            if (query.StartDate.HasValue)
                items = items.Where(x => x.Timestamp >= query.StartDate.Value);

            if (query.EndDate.HasValue)
                items = items.Where(x => x.Timestamp <= query.EndDate.Value);

            if (query.EventTypes?.Any() == true)
                items = items.Where(x => query.EventTypes.Contains(x.EventType));

            if (query.Severities?.Any() == true)
                items = items.Where(x => query.Severities.Contains(x.Severity));

            if (query.Outcome.HasValue)
                items = items.Where(x => x.Outcome == query.Outcome.Value);

            if (query.UserId.HasValue)
                items = items.Where(x => x.UserId == query.UserId.Value);

            if (!string.IsNullOrEmpty(query.Username))
                items = items.Where(x => x.Username?.Contains(query.Username, StringComparison.OrdinalIgnoreCase) == true);

            if (!string.IsNullOrEmpty(query.IpAddressHash))
                items = items.Where(x => x.IpAddressHash == query.IpAddressHash);

            if (!string.IsNullOrEmpty(query.SessionId))
                items = items.Where(x => x.SessionId == query.SessionId);

            if (!string.IsNullOrEmpty(query.CorrelationId))
                items = items.Where(x => x.CorrelationId == query.CorrelationId);

            if (!string.IsNullOrEmpty(query.RequestId))
                items = items.Where(x => x.RequestId == query.RequestId);

            if (!string.IsNullOrEmpty(query.SearchText))
                items = items.Where(x => x.Message?.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) == true);

            if (query.Tags?.Any() == true)
                items = items.Where(x => x.Tags?.Any(t => query.Tags.Contains(t)) == true);

            if (!string.IsNullOrEmpty(query.CountryCode))
                items = items.Where(x => x.CountryCode == query.CountryCode);

            // Get total count before paging
            var totalCount = items.Count();

            // Apply sorting
            items = query.SortBy?.ToLower() switch
            {
                "eventtype" => query.SortDescending ? items.OrderByDescending(x => x.EventType) : items.OrderBy(x => x.EventType),
                "severity" => query.SortDescending ? items.OrderByDescending(x => x.Severity) : items.OrderBy(x => x.Severity),
                "userid" => query.SortDescending ? items.OrderByDescending(x => x.UserId) : items.OrderBy(x => x.UserId),
                _ => query.SortDescending ? items.OrderByDescending(x => x.Timestamp) : items.OrderBy(x => x.Timestamp)
            };

            // Apply paging
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 500);
            items = items.Skip((page - 1) * pageSize).Take(pageSize);

            return new SecurityLogResult
            {
                Items = items.ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        /// <inheritdoc />
        public List<SecurityLogEntry> GetByCorrelationId(string correlationId)
        {
            if (string.IsNullOrEmpty(correlationId))
                return new List<SecurityLogEntry>();

            lock (_indexLock)
            {
                if (_correlationIndex.TryGetValue(correlationId, out var ids))
                {
                    return ids
                        .Select(id => _logs.TryGetValue(id, out var log) ? log : null)
                        .Where(x => x != null)
                        .OrderBy(x => x.Timestamp)
                        .ToList();
                }
            }
            return new List<SecurityLogEntry>();
        }

        /// <inheritdoc />
        public List<SecurityLogEntry> GetByRequestId(string requestId)
        {
            if (string.IsNullOrEmpty(requestId))
                return new List<SecurityLogEntry>();

            return _logs.Values
                .Where(x => x.RequestId == requestId)
                .OrderBy(x => x.Timestamp)
                .ToList();
        }

        /// <inheritdoc />
        public List<SecurityLogEntry> GetUserLogs(int userId, int count = 100)
        {
            lock (_indexLock)
            {
                if (_userLogIndex.TryGetValue(userId, out var ids))
                {
                    return ids
                        .OrderByDescending(x => x)
                        .Take(count)
                        .Select(id => _logs.TryGetValue(id, out var log) ? log : null)
                        .Where(x => x != null)
                        .ToList();
                }
            }
            return new List<SecurityLogEntry>();
        }

        /// <inheritdoc />
        public List<SecurityLogEntry> GetIpLogs(string ipAddressHash, int count = 100)
        {
            if (string.IsNullOrEmpty(ipAddressHash))
                return new List<SecurityLogEntry>();

            lock (_indexLock)
            {
                if (_ipLogIndex.TryGetValue(ipAddressHash, out var ids))
                {
                    return ids
                        .OrderByDescending(x => x)
                        .Take(count)
                        .Select(id => _logs.TryGetValue(id, out var log) ? log : null)
                        .Where(x => x != null)
                        .ToList();
                }
            }
            return new List<SecurityLogEntry>();
        }

        /// <inheritdoc />
        public SecurityStatistics GetStatistics(DateTime startDate, DateTime endDate)
        {
            var logs = _logs.Values
                .Where(x => x.Timestamp >= startDate && x.Timestamp <= endDate)
                .ToList();

            var stats = new SecurityStatistics
            {
                PeriodStart = startDate,
                PeriodEnd = endDate
            };

            // Login stats
            var loginLogs = logs.Where(x => x.EventType == SecurityEventType.LoginSuccess || x.EventType == SecurityEventType.LoginFailed).ToList();
            stats.TotalLoginAttempts = loginLogs.Count;
            stats.SuccessfulLogins = loginLogs.Count(x => x.EventType == SecurityEventType.LoginSuccess);
            stats.FailedLogins = loginLogs.Count(x => x.EventType == SecurityEventType.LoginFailed);

            // MFA stats
            stats.MfaVerifications = logs.Count(x => x.EventType == SecurityEventType.MfaVerifySuccess);
            stats.MfaFailures = logs.Count(x => x.EventType == SecurityEventType.MfaVerifyFailed);
            stats.RecoveryCodesUsed = logs.Count(x => x.EventType == SecurityEventType.MfaRecoveryCodeUsed);

            // Security events
            stats.RateLimitExceeded = logs.Count(x => x.EventType == SecurityEventType.RateLimitExceeded);
            stats.SuspiciousActivities = logs.Count(x => x.EventType == SecurityEventType.SuspiciousActivity);
            stats.BlockedIps = logs.Count(x => x.EventType == SecurityEventType.IpBlocked);
            stats.BruteForceAttempts = logs.Count(x => x.EventType == SecurityEventType.BruteForceDetected);

            // Account events
            stats.NewAccounts = logs.Count(x => x.EventType == SecurityEventType.AccountCreated);
            stats.AccountsLocked = logs.Count(x => x.EventType == SecurityEventType.AccountLocked);
            stats.PasswordChanges = logs.Count(x => x.EventType == SecurityEventType.PasswordChanged);

            // By severity
            stats.BySeverity = logs
                .GroupBy(x => x.Severity)
                .ToDictionary(g => g.Key, g => g.Count());

            // By event type
            stats.ByEventType = logs
                .GroupBy(x => x.EventType)
                .ToDictionary(g => g.Key, g => g.Count());

            // Top active IPs
            stats.TopActiveIps = logs
                .Where(x => !string.IsNullOrEmpty(x.IpAddressHash))
                .GroupBy(x => x.IpAddressHash)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => new IpActivitySummary
                {
                    IpAddressHash = g.Key,
                    IpAddressMasked = g.First().IpAddress,
                    TotalRequests = g.Count(),
                    FailedAttempts = g.Count(x => x.Outcome == EventOutcome.Failure),
                    SuccessfulAttempts = g.Count(x => x.Outcome == EventOutcome.Success),
                    FirstSeen = g.Min(x => x.Timestamp),
                    LastSeen = g.Max(x => x.Timestamp),
                    Countries = g.Where(x => !string.IsNullOrEmpty(x.CountryCode)).Select(x => x.CountryCode).Distinct().ToList()
                })
                .ToList();

            // Top failed login IPs
            stats.TopFailedLoginIps = logs
                .Where(x => x.EventType == SecurityEventType.LoginFailed && !string.IsNullOrEmpty(x.IpAddressHash))
                .GroupBy(x => x.IpAddressHash)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => new IpActivitySummary
                {
                    IpAddressHash = g.Key,
                    IpAddressMasked = g.First().IpAddress,
                    FailedAttempts = g.Count(),
                    FirstSeen = g.Min(x => x.Timestamp),
                    LastSeen = g.Max(x => x.Timestamp)
                })
                .ToList();

            // By country
            stats.ByCountry = logs
                .Where(x => !string.IsNullOrEmpty(x.CountryCode))
                .GroupBy(x => x.CountryCode)
                .ToDictionary(g => g.Key, g => g.Count());

            // By hour of day
            stats.ByHourOfDay = logs
                .GroupBy(x => x.Timestamp.Hour)
                .ToDictionary(g => g.Key, g => g.Count());

            // By device type
            stats.ByDeviceType = logs
                .Where(x => !string.IsNullOrEmpty(x.DeviceType))
                .GroupBy(x => x.DeviceType)
                .ToDictionary(g => g.Key, g => g.Count());

            return stats;
        }

        /// <inheritdoc />
        public UserActivitySummary GetUserActivity(int userId, DateTime? since = null)
        {
            var sinceDate = since ?? DateTime.UtcNow.AddDays(-30);
            var logs = GetUserLogs(userId, 1000)
                .Where(x => x.Timestamp >= sinceDate)
                .ToList();

            var loginLogs = logs.Where(x =>
                x.EventType == SecurityEventType.LoginSuccess ||
                x.EventType == SecurityEventType.LoginFailed).ToList();

            return new UserActivitySummary
            {
                UserId = userId,
                Username = logs.FirstOrDefault()?.Username,
                TotalLogins = loginLogs.Count(x => x.EventType == SecurityEventType.LoginSuccess),
                FailedLogins = loginLogs.Count(x => x.EventType == SecurityEventType.LoginFailed),
                LastLogin = loginLogs.Where(x => x.EventType == SecurityEventType.LoginSuccess).Max(x => (DateTime?)x.Timestamp),
                LastFailedLogin = loginLogs.Where(x => x.EventType == SecurityEventType.LoginFailed).Max(x => (DateTime?)x.Timestamp),
                UniqueIps = logs.Where(x => !string.IsNullOrEmpty(x.IpAddressHash)).Select(x => x.IpAddressHash).Distinct().Count(),
                UniqueDevices = logs.Where(x => !string.IsNullOrEmpty(x.Fingerprint)).Select(x => x.Fingerprint).Distinct().Count(),
                RecentIps = logs.Where(x => !string.IsNullOrEmpty(x.IpAddress)).Select(x => x.IpAddress).Distinct().Take(5).ToList(),
                RecentCountries = logs.Where(x => !string.IsNullOrEmpty(x.CountryCode)).Select(x => x.CountryCode).Distinct().Take(5).ToList()
            };
        }

        /// <inheritdoc />
        public IpActivitySummary GetIpActivity(string ipAddressHash, DateTime? since = null)
        {
            if (string.IsNullOrEmpty(ipAddressHash))
                return null;

            var sinceDate = since ?? DateTime.UtcNow.AddDays(-30);
            var logs = GetIpLogs(ipAddressHash, 1000)
                .Where(x => x.Timestamp >= sinceDate)
                .ToList();

            if (!logs.Any())
                return null;

            return new IpActivitySummary
            {
                IpAddressHash = ipAddressHash,
                IpAddressMasked = logs.First().IpAddress,
                TotalRequests = logs.Count,
                FailedAttempts = logs.Count(x => x.Outcome == EventOutcome.Failure),
                SuccessfulAttempts = logs.Count(x => x.Outcome == EventOutcome.Success),
                FirstSeen = logs.Min(x => x.Timestamp),
                LastSeen = logs.Max(x => x.Timestamp),
                Countries = logs.Where(x => !string.IsNullOrEmpty(x.CountryCode)).Select(x => x.CountryCode).Distinct().ToList()
            };
        }

        /// <inheritdoc />
        public int DeleteOlderThan(DateTime cutoff)
        {
            var toDelete = _logs.Values
                .Where(x => x.Timestamp < cutoff)
                .Select(x => x.Id)
                .ToList();

            foreach (var id in toDelete)
            {
                if (_logs.TryRemove(id, out var entry))
                {
                    RemoveFromIndexes(entry);
                }
            }

            return toDelete.Count;
        }

        /// <inheritdoc />
        public int DeleteByUserId(int userId)
        {
            lock (_indexLock)
            {
                if (_userLogIndex.TryRemove(userId, out var ids))
                {
                    foreach (var id in ids)
                    {
                        if (_logs.TryRemove(id, out var entry))
                        {
                            // Remove from other indexes
                            if (!string.IsNullOrEmpty(entry.IpAddressHash) && _ipLogIndex.TryGetValue(entry.IpAddressHash, out var ipLogs))
                            {
                                ipLogs.Remove(id);
                            }
                            if (!string.IsNullOrEmpty(entry.CorrelationId) && _correlationIndex.TryGetValue(entry.CorrelationId, out var corrLogs))
                            {
                                corrLogs.Remove(id);
                            }
                        }
                    }
                    return ids.Count;
                }
            }
            return 0;
        }

        /// <inheritdoc />
        public long GetTotalCount() => _logs.Count;

        /// <inheritdoc />
        public Dictionary<SecurityEventType, int> GetCountByEventType(DateTime? since = null)
        {
            var items = _logs.Values.AsEnumerable();
            if (since.HasValue)
                items = items.Where(x => x.Timestamp >= since.Value);

            return items
                .GroupBy(x => x.EventType)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        private void RemoveFromIndexes(SecurityLogEntry entry)
        {
            lock (_indexLock)
            {
                if (entry.UserId.HasValue && _userLogIndex.TryGetValue(entry.UserId.Value, out var userLogs))
                {
                    userLogs.Remove(entry.Id);
                }
                if (!string.IsNullOrEmpty(entry.IpAddressHash) && _ipLogIndex.TryGetValue(entry.IpAddressHash, out var ipLogs))
                {
                    ipLogs.Remove(entry.Id);
                }
                if (!string.IsNullOrEmpty(entry.CorrelationId) && _correlationIndex.TryGetValue(entry.CorrelationId, out var corrLogs))
                {
                    corrLogs.Remove(entry.Id);
                }
            }
        }

        private void EnforceMaxEntries()
        {
            while (_logs.Count > _maxEntries)
            {
                var oldest = _logs.Values.OrderBy(x => x.Timestamp).FirstOrDefault();
                if (oldest != null && _logs.TryRemove(oldest.Id, out var entry))
                {
                    RemoveFromIndexes(entry);
                }
            }
        }

        private void Cleanup(object state)
        {
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            DeleteOlderThan(cutoff);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cleanupTimer?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// In-memory login record repository
    /// </summary>
    public class InMemoryLoginRecordRepository : ILoginRecordRepository, IDisposable
    {
        private readonly ConcurrentDictionary<long, LoginRecord> _records = new();
        private readonly ConcurrentDictionary<int, List<long>> _userIndex = new();
        private readonly ConcurrentDictionary<string, List<long>> _ipIndex = new();
        private readonly ConcurrentDictionary<string, HashSet<int>> _deviceUserIndex = new(); // fingerprint -> userIds
        private long _idCounter = 0;
        private readonly object _indexLock = new();
        private readonly Timer _cleanupTimer;
        private readonly int _maxRecords;
        private readonly int _retentionDays;
        private bool _disposed;

        public InMemoryLoginRecordRepository(int maxRecords = 50000, int retentionDays = 90)
        {
            _maxRecords = maxRecords;
            _retentionDays = retentionDays;
            _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }

        /// <inheritdoc />
        public long AddLoginRecord(LoginRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.Id = Interlocked.Increment(ref _idCounter);
            if (record.Timestamp == default)
                record.Timestamp = DateTime.UtcNow;

            _records[record.Id] = record;

            lock (_indexLock)
            {
                if (record.UserId.HasValue)
                {
                    if (!_userIndex.TryGetValue(record.UserId.Value, out var userRecords))
                    {
                        userRecords = new List<long>();
                        _userIndex[record.UserId.Value] = userRecords;
                    }
                    userRecords.Add(record.Id);

                    // Track device
                    if (!string.IsNullOrEmpty(record.Fingerprint))
                    {
                        if (!_deviceUserIndex.TryGetValue(record.Fingerprint, out var users))
                        {
                            users = new HashSet<int>();
                            _deviceUserIndex[record.Fingerprint] = users;
                        }
                        users.Add(record.UserId.Value);
                    }
                }

                if (!string.IsNullOrEmpty(record.IpAddressHash))
                {
                    if (!_ipIndex.TryGetValue(record.IpAddressHash, out var ipRecords))
                    {
                        ipRecords = new List<long>();
                        _ipIndex[record.IpAddressHash] = ipRecords;
                    }
                    ipRecords.Add(record.Id);
                }
            }

            EnforceMaxRecords();

            return record.Id;
        }

        /// <inheritdoc />
        public LoginRecord GetById(long id)
        {
            _records.TryGetValue(id, out var record);
            return record;
        }

        /// <inheritdoc />
        public LoginHistoryResult GetUserLoginHistory(int userId, int page = 1, int pageSize = 20)
        {
            List<long> ids;
            lock (_indexLock)
            {
                if (!_userIndex.TryGetValue(userId, out ids))
                    return new LoginHistoryResult { Page = page, PageSize = pageSize };
                ids = ids.ToList(); // Copy
            }

            var totalCount = ids.Count;
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var items = ids
                .OrderByDescending(x => x)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(id => _records.TryGetValue(id, out var r) ? r : null)
                .Where(x => x != null)
                .ToList();

            return new LoginHistoryResult
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        /// <inheritdoc />
        public LoginHistoryResult GetIpLoginHistory(string ipAddressHash, int page = 1, int pageSize = 20)
        {
            if (string.IsNullOrEmpty(ipAddressHash))
                return new LoginHistoryResult { Page = page, PageSize = pageSize };

            List<long> ids;
            lock (_indexLock)
            {
                if (!_ipIndex.TryGetValue(ipAddressHash, out ids))
                    return new LoginHistoryResult { Page = page, PageSize = pageSize };
                ids = ids.ToList();
            }

            var totalCount = ids.Count;
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var items = ids
                .OrderByDescending(x => x)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(id => _records.TryGetValue(id, out var r) ? r : null)
                .Where(x => x != null)
                .ToList();

            return new LoginHistoryResult
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        /// <inheritdoc />
        public List<LoginRecord> GetRecentFailedLogins(int userId, int count = 10)
        {
            lock (_indexLock)
            {
                if (!_userIndex.TryGetValue(userId, out var ids))
                    return new List<LoginRecord>();

                return ids
                    .OrderByDescending(x => x)
                    .Select(id => _records.TryGetValue(id, out var r) ? r : null)
                    .Where(x => x != null && !x.Success)
                    .Take(count)
                    .ToList();
            }
        }

        /// <inheritdoc />
        public List<LoginRecord> GetRecentFailedLoginsByIp(string ipAddressHash, int count = 10)
        {
            if (string.IsNullOrEmpty(ipAddressHash))
                return new List<LoginRecord>();

            lock (_indexLock)
            {
                if (!_ipIndex.TryGetValue(ipAddressHash, out var ids))
                    return new List<LoginRecord>();

                return ids
                    .OrderByDescending(x => x)
                    .Select(id => _records.TryGetValue(id, out var r) ? r : null)
                    .Where(x => x != null && !x.Success)
                    .Take(count)
                    .ToList();
            }
        }

        /// <inheritdoc />
        public List<LoginRecord> GetRecentSuccessfulLogins(int userId, int count = 10)
        {
            lock (_indexLock)
            {
                if (!_userIndex.TryGetValue(userId, out var ids))
                    return new List<LoginRecord>();

                return ids
                    .OrderByDescending(x => x)
                    .Select(id => _records.TryGetValue(id, out var r) ? r : null)
                    .Where(x => x != null && x.Success)
                    .Take(count)
                    .ToList();
            }
        }

        /// <inheritdoc />
        public List<LoginRecord> GetSuspiciousLogins(DateTime since, int count = 100)
        {
            return _records.Values
                .Where(x => x.IsSuspicious && x.Timestamp >= since)
                .OrderByDescending(x => x.Timestamp)
                .Take(count)
                .ToList();
        }

        /// <inheritdoc />
        public bool IsNewDevice(int userId, string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint))
                return true;

            lock (_indexLock)
            {
                if (_deviceUserIndex.TryGetValue(fingerprint, out var users))
                {
                    return !users.Contains(userId);
                }
            }
            return true;
        }

        /// <inheritdoc />
        public List<string> GetUserDevices(int userId)
        {
            lock (_indexLock)
            {
                if (!_userIndex.TryGetValue(userId, out var ids))
                    return new List<string>();

                return ids
                    .Select(id => _records.TryGetValue(id, out var r) ? r : null)
                    .Where(x => x != null && !string.IsNullOrEmpty(x.Fingerprint))
                    .Select(x => x.Fingerprint)
                    .Distinct()
                    .ToList();
            }
        }

        /// <inheritdoc />
        public int DeleteOlderThan(DateTime cutoff)
        {
            var toDelete = _records.Values
                .Where(x => x.Timestamp < cutoff)
                .Select(x => x.Id)
                .ToList();

            foreach (var id in toDelete)
            {
                if (_records.TryRemove(id, out var record))
                {
                    RemoveFromIndexes(record);
                }
            }

            return toDelete.Count;
        }

        private void RemoveFromIndexes(LoginRecord record)
        {
            lock (_indexLock)
            {
                if (record.UserId.HasValue && _userIndex.TryGetValue(record.UserId.Value, out var userRecords))
                {
                    userRecords.Remove(record.Id);
                }
                if (!string.IsNullOrEmpty(record.IpAddressHash) && _ipIndex.TryGetValue(record.IpAddressHash, out var ipRecords))
                {
                    ipRecords.Remove(record.Id);
                }
            }
        }

        private void EnforceMaxRecords()
        {
            while (_records.Count > _maxRecords)
            {
                var oldest = _records.Values.OrderBy(x => x.Timestamp).FirstOrDefault();
                if (oldest != null && _records.TryRemove(oldest.Id, out var record))
                {
                    RemoveFromIndexes(record);
                }
            }
        }

        private void Cleanup(object state)
        {
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            DeleteOlderThan(cutoff);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cleanupTimer?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// In-memory security alert repository
    /// </summary>
    public class InMemorySecurityAlertRepository : ISecurityAlertRepository
    {
        private readonly ConcurrentDictionary<int, SecurityAlertConfig> _configs = new();
        private readonly ConcurrentDictionary<long, SecurityAlert> _alerts = new();
        private int _configIdCounter = 0;
        private long _alertIdCounter = 0;

        /// <inheritdoc />
        public int AddAlertConfig(SecurityAlertConfig config)
        {
            config.Id = Interlocked.Increment(ref _configIdCounter);
            config.CreatedAt = DateTime.UtcNow;
            _configs[config.Id] = config;
            return config.Id;
        }

        /// <inheritdoc />
        public void UpdateAlertConfig(SecurityAlertConfig config)
        {
            if (config != null && _configs.ContainsKey(config.Id))
            {
                _configs[config.Id] = config;
            }
        }

        /// <inheritdoc />
        public SecurityAlertConfig GetAlertConfig(int id)
        {
            _configs.TryGetValue(id, out var config);
            return config;
        }

        /// <inheritdoc />
        public List<SecurityAlertConfig> GetAlertConfigs(bool enabledOnly = false)
        {
            var items = _configs.Values.AsEnumerable();
            if (enabledOnly)
                items = items.Where(x => x.IsEnabled);
            return items.ToList();
        }

        /// <inheritdoc />
        public void DeleteAlertConfig(int id)
        {
            _configs.TryRemove(id, out _);
        }

        /// <inheritdoc />
        public long AddAlert(SecurityAlert alert)
        {
            alert.Id = Interlocked.Increment(ref _alertIdCounter);
            alert.TriggeredAt = DateTime.UtcNow;
            _alerts[alert.Id] = alert;
            return alert.Id;
        }

        /// <inheritdoc />
        public SecurityAlert GetAlert(long id)
        {
            _alerts.TryGetValue(id, out var alert);
            return alert;
        }

        /// <inheritdoc />
        public List<SecurityAlert> GetUnacknowledgedAlerts()
        {
            return _alerts.Values
                .Where(x => !x.IsAcknowledged)
                .OrderByDescending(x => x.TriggeredAt)
                .ToList();
        }

        /// <inheritdoc />
        public List<SecurityAlert> GetRecentAlerts(int count = 50)
        {
            return _alerts.Values
                .OrderByDescending(x => x.TriggeredAt)
                .Take(count)
                .ToList();
        }

        /// <inheritdoc />
        public void AcknowledgeAlert(long alertId, int userId, string note)
        {
            if (_alerts.TryGetValue(alertId, out var alert))
            {
                alert.IsAcknowledged = true;
                alert.AcknowledgedByUserId = userId;
                alert.AcknowledgedAt = DateTime.UtcNow;
                alert.AcknowledgementNote = note;
            }
        }

        /// <inheritdoc />
        public int DeleteOlderThan(DateTime cutoff)
        {
            var toDelete = _alerts.Values
                .Where(x => x.TriggeredAt < cutoff)
                .Select(x => x.Id)
                .ToList();

            foreach (var id in toDelete)
            {
                _alerts.TryRemove(id, out _);
            }

            return toDelete.Count;
        }
    }
}
