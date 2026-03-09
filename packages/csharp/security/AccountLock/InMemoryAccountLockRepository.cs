using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bricks4Agent.Security.AccountLock.Models;

namespace Bricks4Agent.Security.AccountLock
{
    /// <summary>
    /// In-memory account lock repository
    /// For production, use a database-backed implementation
    /// </summary>
    public class InMemoryAccountLockRepository : IAccountLockRepository, IDisposable
    {
        private readonly ConcurrentDictionary<long, AccountLockRecord> _accountLocks = new();
        private readonly ConcurrentDictionary<int, List<long>> _userLockIndex = new();
        private readonly ConcurrentDictionary<long, IpLock> _ipLocks = new();
        private readonly ConcurrentDictionary<string, long> _ipLockIndex = new(); // ipHash -> lockId

        private long _accountLockIdCounter = 0;
        private long _ipLockIdCounter = 0;
        private readonly object _indexLock = new();
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        public InMemoryAccountLockRepository()
        {
            // Auto-cleanup every 5 minutes
            _cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        #region Account Locks

        /// <inheritdoc />
        public long AddLock(AccountLockRecord lockRecord)
        {
            if (lockRecord == null)
                throw new ArgumentNullException(nameof(lockRecord));

            lockRecord.Id = Interlocked.Increment(ref _accountLockIdCounter);
            if (lockRecord.LockedAt == default)
                lockRecord.LockedAt = DateTime.UtcNow;

            _accountLocks[lockRecord.Id] = lockRecord;

            // Update index
            lock (_indexLock)
            {
                if (!_userLockIndex.TryGetValue(lockRecord.UserId, out var locks))
                {
                    locks = new List<long>();
                    _userLockIndex[lockRecord.UserId] = locks;
                }
                locks.Add(lockRecord.Id);
            }

            return lockRecord.Id;
        }

        /// <inheritdoc />
        public AccountLockRecord GetLock(long lockId)
        {
            _accountLocks.TryGetValue(lockId, out var lockRecord);
            return lockRecord;
        }

        /// <inheritdoc />
        public List<AccountLockRecord> GetActiveLocks(int userId)
        {
            var now = DateTime.UtcNow;

            lock (_indexLock)
            {
                if (!_userLockIndex.TryGetValue(userId, out var lockIds))
                    return new List<AccountLockRecord>();

                return lockIds
                    .Select(id => _accountLocks.TryGetValue(id, out var l) ? l : null)
                    .Where(l => l != null && l.IsActive && (l.ExpiresAt == null || l.ExpiresAt > now))
                    .ToList();
            }
        }

        /// <inheritdoc />
        public AccountLockRecord GetActiveLock(int userId, LockScope scope)
        {
            var now = DateTime.UtcNow;

            lock (_indexLock)
            {
                if (!_userLockIndex.TryGetValue(userId, out var lockIds))
                    return null;

                return lockIds
                    .Select(id => _accountLocks.TryGetValue(id, out var l) ? l : null)
                    .FirstOrDefault(l => l != null && l.IsActive && l.Scope == scope &&
                        (l.ExpiresAt == null || l.ExpiresAt > now));
            }
        }

        /// <inheritdoc />
        public List<AccountLockRecord> GetUserLocks(int userId, int limit = 100)
        {
            lock (_indexLock)
            {
                if (!_userLockIndex.TryGetValue(userId, out var lockIds))
                    return new List<AccountLockRecord>();

                return lockIds
                    .OrderByDescending(id => id)
                    .Take(limit)
                    .Select(id => _accountLocks.TryGetValue(id, out var l) ? l : null)
                    .Where(l => l != null)
                    .ToList();
            }
        }

        /// <inheritdoc />
        public List<LockHistoryEntry> GetLockHistory(int userId, int limit = 50)
        {
            return GetUserLocks(userId, limit)
                .Select(l => new LockHistoryEntry
                {
                    LockId = l.Id,
                    UserId = l.UserId,
                    Username = l.Username,
                    LockType = l.LockType,
                    Scope = l.Scope,
                    Reason = l.Reason,
                    LockedAt = l.LockedAt,
                    ExpiresAt = l.ExpiresAt,
                    UnlockedAt = l.UnlockedAt,
                    UnlockedBy = l.UnlockedBy,
                    WasAutoExpired = l.UnlockedAt.HasValue && l.ExpiresAt.HasValue &&
                        Math.Abs((l.UnlockedAt.Value - l.ExpiresAt.Value).TotalSeconds) < 60
                })
                .ToList();
        }

        /// <inheritdoc />
        public bool HasActiveLock(int userId, LockScope? scope = null)
        {
            var now = DateTime.UtcNow;

            lock (_indexLock)
            {
                if (!_userLockIndex.TryGetValue(userId, out var lockIds))
                    return false;

                return lockIds
                    .Select(id => _accountLocks.TryGetValue(id, out var l) ? l : null)
                    .Any(l => l != null && l.IsActive &&
                        (l.ExpiresAt == null || l.ExpiresAt > now) &&
                        (!scope.HasValue || l.Scope == scope.Value));
            }
        }

        /// <inheritdoc />
        public void UpdateLock(AccountLockRecord lockRecord)
        {
            if (lockRecord != null && _accountLocks.ContainsKey(lockRecord.Id))
            {
                _accountLocks[lockRecord.Id] = lockRecord;
            }
        }

        /// <inheritdoc />
        public void DeactivateLock(long lockId, string unlockedBy, string reason)
        {
            if (_accountLocks.TryGetValue(lockId, out var lockRecord))
            {
                lockRecord.IsActive = false;
                lockRecord.UnlockedAt = DateTime.UtcNow;
                lockRecord.UnlockedBy = unlockedBy;
                lockRecord.UnlockReason = reason;
            }
        }

        /// <inheritdoc />
        public int DeactivateAllLocks(int userId, string unlockedBy, string reason)
        {
            var activeLocks = GetActiveLocks(userId);
            foreach (var lockRecord in activeLocks)
            {
                DeactivateLock(lockRecord.Id, unlockedBy, reason);
            }
            return activeLocks.Count;
        }

        /// <inheritdoc />
        public int DeactivateLocksByScope(int userId, LockScope scope, string unlockedBy, string reason)
        {
            var activeLocks = GetActiveLocks(userId).Where(l => l.Scope == scope).ToList();
            foreach (var lockRecord in activeLocks)
            {
                DeactivateLock(lockRecord.Id, unlockedBy, reason);
            }
            return activeLocks.Count;
        }

        /// <inheritdoc />
        public List<AccountLockRecord> GetExpiredActiveLocks()
        {
            var now = DateTime.UtcNow;
            return _accountLocks.Values
                .Where(l => l.IsActive && l.ExpiresAt.HasValue && l.ExpiresAt.Value <= now)
                .ToList();
        }

        /// <inheritdoc />
        public int GetRecentLocksCount(int userId, TimeSpan window, LockType? lockType = null)
        {
            var cutoff = DateTime.UtcNow - window;

            lock (_indexLock)
            {
                if (!_userLockIndex.TryGetValue(userId, out var lockIds))
                    return 0;

                return lockIds
                    .Select(id => _accountLocks.TryGetValue(id, out var l) ? l : null)
                    .Count(l => l != null && l.LockedAt >= cutoff &&
                        (!lockType.HasValue || l.LockType == lockType.Value));
            }
        }

        /// <inheritdoc />
        public List<AccountLockRecord> GetAllActiveLocks(int page = 1, int pageSize = 50)
        {
            var now = DateTime.UtcNow;
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            return _accountLocks.Values
                .Where(l => l.IsActive && (l.ExpiresAt == null || l.ExpiresAt > now))
                .OrderByDescending(l => l.LockedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        /// <inheritdoc />
        public int GetActiveLocksCount()
        {
            var now = DateTime.UtcNow;
            return _accountLocks.Values
                .Count(l => l.IsActive && (l.ExpiresAt == null || l.ExpiresAt > now));
        }

        #endregion

        #region IP Locks

        /// <inheritdoc />
        public long AddIpLock(IpLock lockRecord)
        {
            if (lockRecord == null)
                throw new ArgumentNullException(nameof(lockRecord));

            lockRecord.Id = Interlocked.Increment(ref _ipLockIdCounter);
            if (lockRecord.LockedAt == default)
                lockRecord.LockedAt = DateTime.UtcNow;

            _ipLocks[lockRecord.Id] = lockRecord;

            if (!string.IsNullOrEmpty(lockRecord.IpAddressHash))
            {
                _ipLockIndex[lockRecord.IpAddressHash] = lockRecord.Id;
            }

            return lockRecord.Id;
        }

        /// <inheritdoc />
        public IpLock GetIpLock(long lockId)
        {
            _ipLocks.TryGetValue(lockId, out var lockRecord);
            return lockRecord;
        }

        /// <inheritdoc />
        public IpLock GetActiveIpLock(string ipAddressHash)
        {
            if (string.IsNullOrEmpty(ipAddressHash))
                return null;

            if (_ipLockIndex.TryGetValue(ipAddressHash, out var lockId))
            {
                if (_ipLocks.TryGetValue(lockId, out var lockRecord))
                {
                    if (lockRecord.IsEffective)
                        return lockRecord;
                }
            }
            return null;
        }

        /// <inheritdoc />
        public bool IsIpLocked(string ipAddressHash)
        {
            return GetActiveIpLock(ipAddressHash) != null;
        }

        /// <inheritdoc />
        public void DeactivateIpLock(long lockId, string unlockedBy)
        {
            if (_ipLocks.TryGetValue(lockId, out var lockRecord))
            {
                lockRecord.IsActive = false;
                lockRecord.UnlockedAt = DateTime.UtcNow;
                lockRecord.UnlockedBy = unlockedBy;

                if (!string.IsNullOrEmpty(lockRecord.IpAddressHash))
                {
                    _ipLockIndex.TryRemove(lockRecord.IpAddressHash, out _);
                }
            }
        }

        /// <inheritdoc />
        public void DeactivateIpLockByHash(string ipAddressHash, string unlockedBy)
        {
            if (_ipLockIndex.TryGetValue(ipAddressHash, out var lockId))
            {
                DeactivateIpLock(lockId, unlockedBy);
            }
        }

        /// <inheritdoc />
        public List<IpLock> GetAllActiveIpLocks(int page = 1, int pageSize = 50)
        {
            var now = DateTime.UtcNow;
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            return _ipLocks.Values
                .Where(l => l.IsActive && (l.ExpiresAt == null || l.ExpiresAt > now))
                .OrderByDescending(l => l.LockedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        /// <inheritdoc />
        public List<IpLock> GetExpiredActiveIpLocks()
        {
            var now = DateTime.UtcNow;
            return _ipLocks.Values
                .Where(l => l.IsActive && l.ExpiresAt.HasValue && l.ExpiresAt.Value <= now)
                .ToList();
        }

        #endregion

        #region Statistics

        /// <inheritdoc />
        public LockStatistics GetStatistics(DateTime startDate, DateTime endDate)
        {
            var locks = _accountLocks.Values
                .Where(l => l.LockedAt >= startDate && l.LockedAt <= endDate)
                .ToList();

            var now = DateTime.UtcNow;
            var stats = new LockStatistics
            {
                PeriodStart = startDate,
                PeriodEnd = endDate,
                TotalLocks = locks.Count,
                ActiveLocks = locks.Count(l => l.IsActive && (l.ExpiresAt == null || l.ExpiresAt > now)),
                AutoLocks = locks.Count(l => l.LockType != LockType.Manual),
                ManualLocks = locks.Count(l => l.LockType == LockType.Manual),
                ExpiredLocks = locks.Count(l => !l.IsActive && l.ExpiresAt.HasValue && l.UnlockedAt.HasValue &&
                    Math.Abs((l.UnlockedAt.Value - l.ExpiresAt.Value).TotalSeconds) < 60),
                ManualUnlocks = locks.Count(l => !l.IsActive && !string.IsNullOrEmpty(l.UnlockedBy) &&
                    l.UnlockedBy != "System" && l.UnlockedBy != "AutoExpire")
            };

            stats.ByLockType = locks
                .GroupBy(l => l.LockType)
                .ToDictionary(g => g.Key, g => g.Count());

            stats.ByScope = locks
                .GroupBy(l => l.Scope)
                .ToDictionary(g => g.Key, g => g.Count());

            stats.UniqueUsersLocked = locks.Select(l => l.UserId).Distinct().Count();
            stats.UniqueIpsLocked = _ipLocks.Values
                .Count(l => l.LockedAt >= startDate && l.LockedAt <= endDate);

            var completedLocks = locks.Where(l => !l.IsActive && l.UnlockedAt.HasValue).ToList();
            if (completedLocks.Any())
            {
                stats.AverageLockDurationMinutes = completedLocks
                    .Average(l => (l.UnlockedAt.Value - l.LockedAt).TotalMinutes);
            }

            stats.TopLockedUsers = locks
                .GroupBy(l => new { l.UserId, l.Username })
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => new UserLockSummary
                {
                    UserId = g.Key.UserId,
                    Username = g.Key.Username,
                    LockCount = g.Count(),
                    LastLockAt = g.Max(l => l.LockedAt),
                    IsCurrentlyLocked = g.Any(l => l.IsActive && (l.ExpiresAt == null || l.ExpiresAt > now))
                })
                .ToList();

            return stats;
        }

        #endregion

        #region Cleanup

        /// <inheritdoc />
        public int DeleteOlderThan(DateTime cutoff)
        {
            var toDelete = _accountLocks.Values
                .Where(l => !l.IsActive && l.LockedAt < cutoff)
                .Select(l => l.Id)
                .ToList();

            foreach (var id in toDelete)
            {
                if (_accountLocks.TryRemove(id, out var lockRecord))
                {
                    lock (_indexLock)
                    {
                        if (_userLockIndex.TryGetValue(lockRecord.UserId, out var locks))
                        {
                            locks.Remove(id);
                        }
                    }
                }
            }

            // Also clean up IP locks
            var ipToDelete = _ipLocks.Values
                .Where(l => !l.IsActive && l.LockedAt < cutoff)
                .Select(l => l.Id)
                .ToList();

            foreach (var id in ipToDelete)
            {
                _ipLocks.TryRemove(id, out _);
            }

            return toDelete.Count + ipToDelete.Count;
        }

        private void CleanupExpired(object state)
        {
            // Auto-deactivate expired account locks
            var expiredAccountLocks = GetExpiredActiveLocks();
            foreach (var lockRecord in expiredAccountLocks)
            {
                lockRecord.IsActive = false;
                lockRecord.UnlockedAt = lockRecord.ExpiresAt;
                lockRecord.UnlockedBy = "AutoExpire";
                lockRecord.UnlockReason = "Lock expired automatically";
            }

            // Auto-deactivate expired IP locks
            var expiredIpLocks = GetExpiredActiveIpLocks();
            foreach (var lockRecord in expiredIpLocks)
            {
                DeactivateIpLock(lockRecord.Id, "AutoExpire");
            }

            // Delete very old records (90 days)
            DeleteOlderThan(DateTime.UtcNow.AddDays(-90));
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _cleanupTimer?.Dispose();
                _disposed = true;
            }
        }
    }
}
