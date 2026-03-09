using System;
using System.Collections.Generic;
using Bricks4Agent.Security.AccountLock.Models;

namespace Bricks4Agent.Security.AccountLock
{
    /// <summary>
    /// Account lock repository interface
    /// </summary>
    public interface IAccountLockRepository
    {
        #region Account Locks

        /// <summary>
        /// Add a new account lock
        /// </summary>
        long AddLock(AccountLockRecord lockRecord);

        /// <summary>
        /// Get lock by ID
        /// </summary>
        AccountLockRecord GetLock(long lockId);

        /// <summary>
        /// Get active locks for a user
        /// </summary>
        List<AccountLockRecord> GetActiveLocks(int userId);

        /// <summary>
        /// Get active lock for user with specific scope
        /// </summary>
        AccountLockRecord GetActiveLock(int userId, LockScope scope);

        /// <summary>
        /// Get all locks for a user (including inactive)
        /// </summary>
        List<AccountLockRecord> GetUserLocks(int userId, int limit = 100);

        /// <summary>
        /// Get lock history for a user
        /// </summary>
        List<LockHistoryEntry> GetLockHistory(int userId, int limit = 50);

        /// <summary>
        /// Check if user has active lock
        /// </summary>
        bool HasActiveLock(int userId, LockScope? scope = null);

        /// <summary>
        /// Update lock record
        /// </summary>
        void UpdateLock(AccountLockRecord lockRecord);

        /// <summary>
        /// Deactivate a lock
        /// </summary>
        void DeactivateLock(long lockId, string unlockedBy, string reason);

        /// <summary>
        /// Deactivate all locks for a user
        /// </summary>
        int DeactivateAllLocks(int userId, string unlockedBy, string reason);

        /// <summary>
        /// Deactivate locks by scope
        /// </summary>
        int DeactivateLocksByScope(int userId, LockScope scope, string unlockedBy, string reason);

        /// <summary>
        /// Get expired but still active locks
        /// </summary>
        List<AccountLockRecord> GetExpiredActiveLocks();

        /// <summary>
        /// Get recent locks count for user (for progressive lockout)
        /// </summary>
        int GetRecentLocksCount(int userId, TimeSpan window, LockType? lockType = null);

        /// <summary>
        /// Get all active locks
        /// </summary>
        List<AccountLockRecord> GetAllActiveLocks(int page = 1, int pageSize = 50);

        /// <summary>
        /// Get total active locks count
        /// </summary>
        int GetActiveLocksCount();

        #endregion

        #region IP Locks

        /// <summary>
        /// Add IP lock
        /// </summary>
        long AddIpLock(IpLock lockRecord);

        /// <summary>
        /// Get IP lock
        /// </summary>
        IpLock GetIpLock(long lockId);

        /// <summary>
        /// Get active IP lock by IP hash
        /// </summary>
        IpLock GetActiveIpLock(string ipAddressHash);

        /// <summary>
        /// Check if IP is locked
        /// </summary>
        bool IsIpLocked(string ipAddressHash);

        /// <summary>
        /// Deactivate IP lock
        /// </summary>
        void DeactivateIpLock(long lockId, string unlockedBy);

        /// <summary>
        /// Deactivate IP lock by hash
        /// </summary>
        void DeactivateIpLockByHash(string ipAddressHash, string unlockedBy);

        /// <summary>
        /// Get all active IP locks
        /// </summary>
        List<IpLock> GetAllActiveIpLocks(int page = 1, int pageSize = 50);

        /// <summary>
        /// Get expired active IP locks
        /// </summary>
        List<IpLock> GetExpiredActiveIpLocks();

        #endregion

        #region Statistics

        /// <summary>
        /// Get lock statistics
        /// </summary>
        LockStatistics GetStatistics(DateTime startDate, DateTime endDate);

        #endregion

        #region Cleanup

        /// <summary>
        /// Delete old lock records
        /// </summary>
        int DeleteOlderThan(DateTime cutoff);

        #endregion
    }
}
