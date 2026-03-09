using System;
using System.Collections.Generic;

namespace Bricks4Agent.Security.AccountLock.Models
{
    /// <summary>
    /// Lock type - what triggered the lock
    /// </summary>
    public enum LockType
    {
        /// <summary>
        /// Manual lock by administrator
        /// </summary>
        Manual = 0,

        /// <summary>
        /// Automatic lock due to failed login attempts
        /// </summary>
        FailedLogin = 1,

        /// <summary>
        /// Automatic lock due to failed MFA attempts
        /// </summary>
        FailedMfa = 2,

        /// <summary>
        /// Lock due to suspicious activity
        /// </summary>
        SuspiciousActivity = 3,

        /// <summary>
        /// Lock due to rate limit exceeded
        /// </summary>
        RateLimitExceeded = 4,

        /// <summary>
        /// Lock due to password reset request
        /// </summary>
        PasswordReset = 5,

        /// <summary>
        /// Lock due to security policy violation
        /// </summary>
        PolicyViolation = 6,

        /// <summary>
        /// Lock due to account compromise detected
        /// </summary>
        CompromiseDetected = 7,

        /// <summary>
        /// Lock due to user inactivity (dormant account)
        /// </summary>
        Inactivity = 8,

        /// <summary>
        /// Temporary maintenance lock
        /// </summary>
        Maintenance = 9
    }

    /// <summary>
    /// Lock scope - what is being locked
    /// </summary>
    public enum LockScope
    {
        /// <summary>
        /// Lock entire account
        /// </summary>
        Account = 0,

        /// <summary>
        /// Lock login only (can still use existing sessions)
        /// </summary>
        Login = 1,

        /// <summary>
        /// Lock specific IP address for this account
        /// </summary>
        IpAddress = 2,

        /// <summary>
        /// Lock MFA verification
        /// </summary>
        Mfa = 3,

        /// <summary>
        /// Lock password change
        /// </summary>
        PasswordChange = 4,

        /// <summary>
        /// Lock sensitive operations
        /// </summary>
        SensitiveOperations = 5
    }

    /// <summary>
    /// Account lock record
    /// </summary>
    public class AccountLockRecord
    {
        /// <summary>
        /// Unique lock ID
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// User ID being locked
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Username/email for display
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Type of lock
        /// </summary>
        public LockType LockType { get; set; }

        /// <summary>
        /// Scope of the lock
        /// </summary>
        public LockScope Scope { get; set; }

        /// <summary>
        /// Reason for the lock
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Detailed description
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// When the lock was created
        /// </summary>
        public DateTime LockedAt { get; set; }

        /// <summary>
        /// When the lock expires (null = permanent until manual unlock)
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Whether the lock is currently active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When the lock was released (if released)
        /// </summary>
        public DateTime? UnlockedAt { get; set; }

        /// <summary>
        /// Who/what unlocked the account
        /// </summary>
        public string UnlockedBy { get; set; }

        /// <summary>
        /// Reason for unlock
        /// </summary>
        public string UnlockReason { get; set; }

        /// <summary>
        /// Admin user ID who created the lock (for manual locks)
        /// </summary>
        public int? LockedByUserId { get; set; }

        /// <summary>
        /// Admin username who created the lock
        /// </summary>
        public string LockedByUsername { get; set; }

        /// <summary>
        /// IP address that triggered the lock
        /// </summary>
        public string TriggerIpAddress { get; set; }

        /// <summary>
        /// Number of failed attempts that led to this lock
        /// </summary>
        public int? FailedAttempts { get; set; }

        /// <summary>
        /// Related IP address (for IP-scoped locks)
        /// </summary>
        public string LockedIpAddress { get; set; }

        /// <summary>
        /// Additional metadata (JSON)
        /// </summary>
        public string Metadata { get; set; }

        /// <summary>
        /// Check if this lock is currently in effect
        /// </summary>
        public bool IsEffective => IsActive && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);

        /// <summary>
        /// Time remaining until expiration
        /// </summary>
        public TimeSpan? TimeRemaining => ExpiresAt.HasValue && IsActive
            ? (ExpiresAt.Value > DateTime.UtcNow ? ExpiresAt.Value - DateTime.UtcNow : TimeSpan.Zero)
            : null;
    }

    /// <summary>
    /// IP address lock record
    /// </summary>
    public class IpLock
    {
        /// <summary>
        /// Unique lock ID
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// IP address being locked
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// IP address hash for searching
        /// </summary>
        public string IpAddressHash { get; set; }

        /// <summary>
        /// Type of lock
        /// </summary>
        public LockType LockType { get; set; }

        /// <summary>
        /// Reason for the lock
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// When the lock was created
        /// </summary>
        public DateTime LockedAt { get; set; }

        /// <summary>
        /// When the lock expires
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Whether the lock is currently active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When the lock was released
        /// </summary>
        public DateTime? UnlockedAt { get; set; }

        /// <summary>
        /// Who unlocked
        /// </summary>
        public string UnlockedBy { get; set; }

        /// <summary>
        /// Admin user ID who created the lock
        /// </summary>
        public int? LockedByUserId { get; set; }

        /// <summary>
        /// Number of affected users
        /// </summary>
        public int AffectedUserCount { get; set; }

        /// <summary>
        /// Number of failed attempts from this IP
        /// </summary>
        public int FailedAttempts { get; set; }

        /// <summary>
        /// Check if this lock is currently in effect
        /// </summary>
        public bool IsEffective => IsActive && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);
    }

    /// <summary>
    /// Lock history entry
    /// </summary>
    public class LockHistoryEntry
    {
        public long LockId { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; }
        public LockType LockType { get; set; }
        public LockScope Scope { get; set; }
        public string Reason { get; set; }
        public DateTime LockedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? UnlockedAt { get; set; }
        public string UnlockedBy { get; set; }
        public TimeSpan Duration => (UnlockedAt ?? DateTime.UtcNow) - LockedAt;
        public bool WasAutoExpired { get; set; }
    }

    /// <summary>
    /// Lock status for a user
    /// </summary>
    public class UserLockStatus
    {
        /// <summary>
        /// User ID
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Username
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Whether the account is currently locked
        /// </summary>
        public bool IsLocked { get; set; }

        /// <summary>
        /// Active locks
        /// </summary>
        public List<AccountLockRecord> ActiveLocks { get; set; } = new();

        /// <summary>
        /// Lock history
        /// </summary>
        public List<LockHistoryEntry> LockHistory { get; set; } = new();

        /// <summary>
        /// Total times the account has been locked
        /// </summary>
        public int TotalLockCount { get; set; }

        /// <summary>
        /// Most severe active lock
        /// </summary>
        public AccountLockRecord PrimaryLock => ActiveLocks?.Count > 0
            ? ActiveLocks.OrderBy(l => l.Scope).ThenBy(l => l.ExpiresAt ?? DateTime.MaxValue).First()
            : null;

        /// <summary>
        /// Earliest unlock time (considering all active locks)
        /// </summary>
        public DateTime? EarliestUnlockTime => ActiveLocks?.Count > 0
            ? ActiveLocks.Where(l => l.ExpiresAt.HasValue).Min(l => l.ExpiresAt)
            : null;

        /// <summary>
        /// Whether any lock is permanent
        /// </summary>
        public bool HasPermanentLock => ActiveLocks?.Any(l => !l.ExpiresAt.HasValue) == true;
    }

    /// <summary>
    /// Lock request
    /// </summary>
    public class LockAccountRequest
    {
        /// <summary>
        /// User ID to lock
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Type of lock
        /// </summary>
        public LockType LockType { get; set; } = LockType.Manual;

        /// <summary>
        /// Scope of lock
        /// </summary>
        public LockScope Scope { get; set; } = LockScope.Account;

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
        /// Specific IP to lock (for IP-scoped locks)
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// Whether to invalidate all existing sessions
        /// </summary>
        public bool InvalidateSessions { get; set; } = false;

        /// <summary>
        /// Whether to send notification to user
        /// </summary>
        public bool NotifyUser { get; set; } = true;
    }

    /// <summary>
    /// Unlock request
    /// </summary>
    public class UnlockAccountRequest
    {
        /// <summary>
        /// User ID to unlock
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Specific lock ID to remove (null = remove all)
        /// </summary>
        public long? LockId { get; set; }

        /// <summary>
        /// Scope to unlock (null = all scopes)
        /// </summary>
        public LockScope? Scope { get; set; }

        /// <summary>
        /// Reason for unlocking
        /// </summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// Lock IP request
    /// </summary>
    public class LockIpRequest
    {
        /// <summary>
        /// IP address to lock
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// Type of lock
        /// </summary>
        public LockType LockType { get; set; } = LockType.Manual;

        /// <summary>
        /// Reason
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Duration in minutes (null = permanent)
        /// </summary>
        public int? DurationMinutes { get; set; }
    }

    /// <summary>
    /// Lock configuration
    /// </summary>
    public class AccountLockConfig
    {
        /// <summary>
        /// Maximum failed login attempts before lock
        /// </summary>
        public int MaxFailedLoginAttempts { get; set; } = 5;

        /// <summary>
        /// Time window for counting failed attempts (minutes)
        /// </summary>
        public int FailedLoginWindowMinutes { get; set; } = 15;

        /// <summary>
        /// Lock duration after failed logins (minutes)
        /// </summary>
        public int FailedLoginLockMinutes { get; set; } = 15;

        /// <summary>
        /// Maximum failed MFA attempts before lock
        /// </summary>
        public int MaxFailedMfaAttempts { get; set; } = 5;

        /// <summary>
        /// Lock duration after failed MFA (minutes)
        /// </summary>
        public int FailedMfaLockMinutes { get; set; } = 30;

        /// <summary>
        /// Progressive lockout - multiply duration for repeat offenders
        /// </summary>
        public bool EnableProgressiveLockout { get; set; } = true;

        /// <summary>
        /// Progressive lockout multipliers
        /// </summary>
        public List<double> ProgressiveMultipliers { get; set; } = new() { 1, 2, 4, 8, 24 };

        /// <summary>
        /// Time window to consider for progressive lockout (hours)
        /// </summary>
        public int ProgressiveWindowHours { get; set; } = 24;

        /// <summary>
        /// Auto-unlock expired locks
        /// </summary>
        public bool AutoUnlockExpired { get; set; } = true;

        /// <summary>
        /// Days of inactivity before dormant account lock
        /// </summary>
        public int? InactiveDaysBeforeLock { get; set; } = null;
    }

    /// <summary>
    /// Lock check result
    /// </summary>
    public class LockCheckResult
    {
        /// <summary>
        /// Whether the account/resource is locked
        /// </summary>
        public bool IsLocked { get; set; }

        /// <summary>
        /// Lock type if locked
        /// </summary>
        public LockType? LockType { get; set; }

        /// <summary>
        /// Lock scope if locked
        /// </summary>
        public LockScope? Scope { get; set; }

        /// <summary>
        /// Reason for lock
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// When the lock expires
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Seconds until unlock
        /// </summary>
        public int? RetryAfterSeconds { get; set; }

        /// <summary>
        /// The active lock (if any)
        /// </summary>
        public AccountLockRecord ActiveLock { get; set; }

        /// <summary>
        /// User-friendly message
        /// </summary>
        public string Message { get; set; }

        public static LockCheckResult NotLocked() => new() { IsLocked = false };

        public static LockCheckResult Locked(AccountLockRecord lockRecord, string message = null) => new()
        {
            IsLocked = true,
            LockType = lockRecord.LockType,
            Scope = lockRecord.Scope,
            Reason = lockRecord.Reason,
            ExpiresAt = lockRecord.ExpiresAt,
            RetryAfterSeconds = lockRecord.ExpiresAt.HasValue
                ? (int)Math.Ceiling((lockRecord.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds)
                : null,
            ActiveLock = lockRecord,
            Message = message ?? GetDefaultMessage(lockRecord)
        };

        private static string GetDefaultMessage(AccountLockRecord lockRecord)
        {
            var baseMessage = lockRecord.LockType switch
            {
                Models.LockType.FailedLogin => "Account temporarily locked due to multiple failed login attempts",
                Models.LockType.FailedMfa => "Account temporarily locked due to multiple failed MFA attempts",
                Models.LockType.SuspiciousActivity => "Account locked due to suspicious activity",
                Models.LockType.Manual => "Account has been locked by an administrator",
                Models.LockType.RateLimitExceeded => "Account temporarily locked due to rate limit",
                Models.LockType.CompromiseDetected => "Account locked for security review",
                _ => "Account is currently locked"
            };

            if (lockRecord.ExpiresAt.HasValue)
            {
                var remaining = lockRecord.ExpiresAt.Value - DateTime.UtcNow;
                if (remaining.TotalMinutes < 1)
                    return $"{baseMessage}. Please try again in a few seconds.";
                if (remaining.TotalMinutes < 60)
                    return $"{baseMessage}. Please try again in {(int)remaining.TotalMinutes} minute(s).";
                return $"{baseMessage}. Please try again in {(int)remaining.TotalHours} hour(s).";
            }

            return $"{baseMessage}. Please contact support.";
        }
    }

    /// <summary>
    /// Lock statistics
    /// </summary>
    public class LockStatistics
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public int TotalLocks { get; set; }
        public int ActiveLocks { get; set; }
        public int AutoLocks { get; set; }
        public int ManualLocks { get; set; }
        public int ExpiredLocks { get; set; }
        public int ManualUnlocks { get; set; }

        public Dictionary<LockType, int> ByLockType { get; set; } = new();
        public Dictionary<LockScope, int> ByScope { get; set; } = new();

        public int UniqueUsersLocked { get; set; }
        public int UniqueIpsLocked { get; set; }

        public double AverageLockDurationMinutes { get; set; }
        public List<UserLockSummary> TopLockedUsers { get; set; } = new();
    }

    /// <summary>
    /// User lock summary
    /// </summary>
    public class UserLockSummary
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public int LockCount { get; set; }
        public DateTime? LastLockAt { get; set; }
        public bool IsCurrentlyLocked { get; set; }
    }
}
