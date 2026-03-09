using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bricks4Agent.Security.AccountLock.Models;

namespace Bricks4Agent.Security.AccountLock
{
    /// <summary>
    /// Account lock service interface
    /// </summary>
    public interface IAccountLockService
    {
        #region Lock Check

        /// <summary>
        /// Check if user account is locked
        /// </summary>
        LockCheckResult CheckLock(int userId, LockScope? scope = null);

        /// <summary>
        /// Check if IP is locked
        /// </summary>
        LockCheckResult CheckIpLock(string ipAddress);

        /// <summary>
        /// Check if user can perform action (combines account and IP check)
        /// </summary>
        LockCheckResult CheckCanPerformAction(int? userId, string ipAddress, LockScope scope = LockScope.Account);

        #endregion

        #region Account Lock Operations

        /// <summary>
        /// Lock a user account
        /// </summary>
        AccountLockRecord LockAccount(LockAccountRequest request, int? adminUserId = null, string adminUsername = null);

        /// <summary>
        /// Lock account due to failed login attempts
        /// </summary>
        AccountLockRecord LockAccountForFailedLogin(int userId, string username, string ipAddress, int failedAttempts);

        /// <summary>
        /// Lock account due to failed MFA attempts
        /// </summary>
        AccountLockRecord LockAccountForFailedMfa(int userId, string username, string ipAddress, int failedAttempts);

        /// <summary>
        /// Lock account for suspicious activity
        /// </summary>
        AccountLockRecord LockAccountForSuspiciousActivity(int userId, string username, string ipAddress, string reason);

        /// <summary>
        /// Unlock a user account
        /// </summary>
        int UnlockAccount(UnlockAccountRequest request, int adminUserId, string adminUsername);

        /// <summary>
        /// Unlock specific lock by ID
        /// </summary>
        bool UnlockById(long lockId, int adminUserId, string adminUsername, string reason);

        #endregion

        #region IP Lock Operations

        /// <summary>
        /// Lock an IP address
        /// </summary>
        IpLock LockIp(LockIpRequest request, int? adminUserId = null);

        /// <summary>
        /// Lock IP due to suspicious activity
        /// </summary>
        IpLock LockIpForSuspiciousActivity(string ipAddress, string reason, int? durationMinutes = null);

        /// <summary>
        /// Unlock an IP address
        /// </summary>
        bool UnlockIp(string ipAddress, int adminUserId, string adminUsername);

        /// <summary>
        /// Unlock IP by lock ID
        /// </summary>
        bool UnlockIpById(long lockId, int adminUserId, string adminUsername);

        #endregion

        #region Status & History

        /// <summary>
        /// Get user's lock status
        /// </summary>
        UserLockStatus GetUserLockStatus(int userId);

        /// <summary>
        /// Get lock history for user
        /// </summary>
        List<LockHistoryEntry> GetLockHistory(int userId, int limit = 50);

        /// <summary>
        /// Get all active account locks
        /// </summary>
        List<AccountLockRecord> GetAllActiveLocks(int page = 1, int pageSize = 50);

        /// <summary>
        /// Get all active IP locks
        /// </summary>
        List<IpLock> GetAllActiveIpLocks(int page = 1, int pageSize = 50);

        /// <summary>
        /// Get lock statistics
        /// </summary>
        LockStatistics GetStatistics(int days = 7);

        #endregion

        #region Failed Attempt Tracking

        /// <summary>
        /// Record failed login attempt and check if should lock
        /// </summary>
        LockCheckResult RecordFailedLogin(int? userId, string username, string ipAddress);

        /// <summary>
        /// Record failed MFA attempt and check if should lock
        /// </summary>
        LockCheckResult RecordFailedMfa(int userId, string username, string ipAddress);

        /// <summary>
        /// Reset failed attempt counter (e.g., after successful login)
        /// </summary>
        void ResetFailedAttempts(int userId, string ipAddress);

        #endregion
    }

    /// <summary>
    /// Account lock service implementation
    /// </summary>
    public class AccountLockService : IAccountLockService
    {
        private readonly IAccountLockRepository _repository;
        private readonly AccountLockConfig _config;
        private readonly IFailedAttemptTracker _attemptTracker;

        // Event for notifying other systems
        public event Action<AccountLockRecord> OnAccountLocked;
        public event Action<AccountLockRecord> OnAccountUnlocked;
        public event Action<IpLock> OnIpLocked;
        public event Action<IpLock> OnIpUnlocked;

        public AccountLockService(
            IAccountLockRepository repository,
            AccountLockConfig config = null,
            IFailedAttemptTracker attemptTracker = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _config = config ?? new AccountLockConfig();
            _attemptTracker = attemptTracker ?? new InMemoryFailedAttemptTracker();
        }

        #region Lock Check

        /// <inheritdoc />
        public LockCheckResult CheckLock(int userId, LockScope? scope = null)
        {
            // Check for Account scope first (most restrictive)
            var accountLock = _repository.GetActiveLock(userId, LockScope.Account);
            if (accountLock != null && accountLock.IsEffective)
            {
                return LockCheckResult.Locked(accountLock);
            }

            // Check for specific scope if provided
            if (scope.HasValue && scope.Value != LockScope.Account)
            {
                var scopedLock = _repository.GetActiveLock(userId, scope.Value);
                if (scopedLock != null && scopedLock.IsEffective)
                {
                    return LockCheckResult.Locked(scopedLock);
                }
            }

            return LockCheckResult.NotLocked();
        }

        /// <inheritdoc />
        public LockCheckResult CheckIpLock(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return LockCheckResult.NotLocked();

            var ipHash = HashIpAddress(ipAddress);
            var ipLock = _repository.GetActiveIpLock(ipHash);

            if (ipLock != null && ipLock.IsEffective)
            {
                return new LockCheckResult
                {
                    IsLocked = true,
                    LockType = ipLock.LockType,
                    Reason = ipLock.Reason,
                    ExpiresAt = ipLock.ExpiresAt,
                    RetryAfterSeconds = ipLock.ExpiresAt.HasValue
                        ? (int)Math.Ceiling((ipLock.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds)
                        : null,
                    Message = "Your IP address has been temporarily blocked"
                };
            }

            return LockCheckResult.NotLocked();
        }

        /// <inheritdoc />
        public LockCheckResult CheckCanPerformAction(int? userId, string ipAddress, LockScope scope = LockScope.Account)
        {
            // Check IP lock first
            var ipCheck = CheckIpLock(ipAddress);
            if (ipCheck.IsLocked)
                return ipCheck;

            // Check account lock if user ID provided
            if (userId.HasValue)
            {
                return CheckLock(userId.Value, scope);
            }

            return LockCheckResult.NotLocked();
        }

        #endregion

        #region Account Lock Operations

        /// <inheritdoc />
        public AccountLockRecord LockAccount(LockAccountRequest request, int? adminUserId = null, string adminUsername = null)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            // Calculate duration with progressive lockout
            var duration = request.DurationMinutes.HasValue
                ? TimeSpan.FromMinutes(request.DurationMinutes.Value)
                : (TimeSpan?)null;

            if (_config.EnableProgressiveLockout && request.LockType != LockType.Manual)
            {
                duration = CalculateProgressiveDuration(request.UserId, request.LockType, duration);
            }

            var lockRecord = new AccountLockRecord
            {
                UserId = request.UserId,
                LockType = request.LockType,
                Scope = request.Scope,
                Reason = request.Reason ?? GetDefaultReason(request.LockType),
                Description = request.Description,
                LockedAt = DateTime.UtcNow,
                ExpiresAt = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : null,
                IsActive = true,
                LockedByUserId = adminUserId,
                LockedByUsername = adminUsername,
                TriggerIpAddress = request.IpAddress,
                LockedIpAddress = request.Scope == LockScope.IpAddress ? request.IpAddress : null
            };

            var lockId = _repository.AddLock(lockRecord);
            lockRecord.Id = lockId;

            OnAccountLocked?.Invoke(lockRecord);

            return lockRecord;
        }

        /// <inheritdoc />
        public AccountLockRecord LockAccountForFailedLogin(int userId, string username, string ipAddress, int failedAttempts)
        {
            var request = new LockAccountRequest
            {
                UserId = userId,
                LockType = LockType.FailedLogin,
                Scope = LockScope.Login,
                Reason = $"Account locked due to {failedAttempts} failed login attempts",
                DurationMinutes = _config.FailedLoginLockMinutes,
                IpAddress = ipAddress
            };

            var lockRecord = LockAccount(request);
            lockRecord.FailedAttempts = failedAttempts;
            lockRecord.Username = username;
            _repository.UpdateLock(lockRecord);

            return lockRecord;
        }

        /// <inheritdoc />
        public AccountLockRecord LockAccountForFailedMfa(int userId, string username, string ipAddress, int failedAttempts)
        {
            var request = new LockAccountRequest
            {
                UserId = userId,
                LockType = LockType.FailedMfa,
                Scope = LockScope.Mfa,
                Reason = $"MFA locked due to {failedAttempts} failed verification attempts",
                DurationMinutes = _config.FailedMfaLockMinutes,
                IpAddress = ipAddress
            };

            var lockRecord = LockAccount(request);
            lockRecord.FailedAttempts = failedAttempts;
            lockRecord.Username = username;
            _repository.UpdateLock(lockRecord);

            return lockRecord;
        }

        /// <inheritdoc />
        public AccountLockRecord LockAccountForSuspiciousActivity(int userId, string username, string ipAddress, string reason)
        {
            var request = new LockAccountRequest
            {
                UserId = userId,
                LockType = LockType.SuspiciousActivity,
                Scope = LockScope.Account,
                Reason = reason ?? "Suspicious activity detected",
                DurationMinutes = 60, // 1 hour by default
                IpAddress = ipAddress
            };

            var lockRecord = LockAccount(request);
            lockRecord.Username = username;
            _repository.UpdateLock(lockRecord);

            return lockRecord;
        }

        /// <inheritdoc />
        public int UnlockAccount(UnlockAccountRequest request, int adminUserId, string adminUsername)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var unlockedBy = adminUsername ?? $"Admin:{adminUserId}";
            int count;

            if (request.LockId.HasValue)
            {
                var success = UnlockById(request.LockId.Value, adminUserId, adminUsername, request.Reason);
                count = success ? 1 : 0;
            }
            else if (request.Scope.HasValue)
            {
                count = _repository.DeactivateLocksByScope(request.UserId, request.Scope.Value, unlockedBy, request.Reason);
            }
            else
            {
                count = _repository.DeactivateAllLocks(request.UserId, unlockedBy, request.Reason);
            }

            // Reset failed attempts
            _attemptTracker.Reset(request.UserId.ToString());

            return count;
        }

        /// <inheritdoc />
        public bool UnlockById(long lockId, int adminUserId, string adminUsername, string reason)
        {
            var lockRecord = _repository.GetLock(lockId);
            if (lockRecord == null || !lockRecord.IsActive)
                return false;

            var unlockedBy = adminUsername ?? $"Admin:{adminUserId}";
            _repository.DeactivateLock(lockId, unlockedBy, reason);

            OnAccountUnlocked?.Invoke(lockRecord);

            return true;
        }

        #endregion

        #region IP Lock Operations

        /// <inheritdoc />
        public IpLock LockIp(LockIpRequest request, int? adminUserId = null)
        {
            if (request == null || string.IsNullOrEmpty(request.IpAddress))
                throw new ArgumentException("IP address is required");

            var ipHash = HashIpAddress(request.IpAddress);

            // Check if already locked
            var existing = _repository.GetActiveIpLock(ipHash);
            if (existing != null)
            {
                // Extend the lock if new request has longer duration
                if (request.DurationMinutes.HasValue && existing.ExpiresAt.HasValue)
                {
                    var newExpiry = DateTime.UtcNow.AddMinutes(request.DurationMinutes.Value);
                    if (newExpiry > existing.ExpiresAt)
                    {
                        existing.ExpiresAt = newExpiry;
                        existing.Reason = request.Reason ?? existing.Reason;
                    }
                }
                return existing;
            }

            var lockRecord = new IpLock
            {
                IpAddress = MaskIpAddress(request.IpAddress),
                IpAddressHash = ipHash,
                LockType = request.LockType,
                Reason = request.Reason ?? "IP address blocked",
                LockedAt = DateTime.UtcNow,
                ExpiresAt = request.DurationMinutes.HasValue
                    ? DateTime.UtcNow.AddMinutes(request.DurationMinutes.Value)
                    : null,
                IsActive = true,
                LockedByUserId = adminUserId
            };

            var lockId = _repository.AddIpLock(lockRecord);
            lockRecord.Id = lockId;

            OnIpLocked?.Invoke(lockRecord);

            return lockRecord;
        }

        /// <inheritdoc />
        public IpLock LockIpForSuspiciousActivity(string ipAddress, string reason, int? durationMinutes = null)
        {
            return LockIp(new LockIpRequest
            {
                IpAddress = ipAddress,
                LockType = LockType.SuspiciousActivity,
                Reason = reason,
                DurationMinutes = durationMinutes ?? 60
            });
        }

        /// <inheritdoc />
        public bool UnlockIp(string ipAddress, int adminUserId, string adminUsername)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return false;

            var ipHash = HashIpAddress(ipAddress);
            var lockRecord = _repository.GetActiveIpLock(ipHash);

            if (lockRecord == null)
                return false;

            var unlockedBy = adminUsername ?? $"Admin:{adminUserId}";
            _repository.DeactivateIpLockByHash(ipHash, unlockedBy);

            // Reset failed attempts for this IP
            _attemptTracker.Reset($"ip:{ipHash}");

            OnIpUnlocked?.Invoke(lockRecord);

            return true;
        }

        /// <inheritdoc />
        public bool UnlockIpById(long lockId, int adminUserId, string adminUsername)
        {
            var lockRecord = _repository.GetIpLock(lockId);
            if (lockRecord == null || !lockRecord.IsActive)
                return false;

            var unlockedBy = adminUsername ?? $"Admin:{adminUserId}";
            _repository.DeactivateIpLock(lockId, unlockedBy);

            OnIpUnlocked?.Invoke(lockRecord);

            return true;
        }

        #endregion

        #region Status & History

        /// <inheritdoc />
        public UserLockStatus GetUserLockStatus(int userId)
        {
            var activeLocks = _repository.GetActiveLocks(userId);
            var history = _repository.GetLockHistory(userId, 20);
            var allLocks = _repository.GetUserLocks(userId, 100);

            return new UserLockStatus
            {
                UserId = userId,
                Username = activeLocks.FirstOrDefault()?.Username ?? history.FirstOrDefault()?.Username,
                IsLocked = activeLocks.Any(),
                ActiveLocks = activeLocks,
                LockHistory = history,
                TotalLockCount = allLocks.Count
            };
        }

        /// <inheritdoc />
        public List<LockHistoryEntry> GetLockHistory(int userId, int limit = 50)
        {
            return _repository.GetLockHistory(userId, limit);
        }

        /// <inheritdoc />
        public List<AccountLockRecord> GetAllActiveLocks(int page = 1, int pageSize = 50)
        {
            return _repository.GetAllActiveLocks(page, pageSize);
        }

        /// <inheritdoc />
        public List<IpLock> GetAllActiveIpLocks(int page = 1, int pageSize = 50)
        {
            return _repository.GetAllActiveIpLocks(page, pageSize);
        }

        /// <inheritdoc />
        public LockStatistics GetStatistics(int days = 7)
        {
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-days);
            return _repository.GetStatistics(startDate, endDate);
        }

        #endregion

        #region Failed Attempt Tracking

        /// <inheritdoc />
        public LockCheckResult RecordFailedLogin(int? userId, string username, string ipAddress)
        {
            // Track by user if known
            if (userId.HasValue)
            {
                var userKey = userId.Value.ToString();
                var userAttempts = _attemptTracker.Increment(userKey);

                if (userAttempts >= _config.MaxFailedLoginAttempts)
                {
                    var lockRecord = LockAccountForFailedLogin(userId.Value, username, ipAddress, userAttempts);
                    _attemptTracker.Reset(userKey);
                    return LockCheckResult.Locked(lockRecord);
                }
            }

            // Also track by IP
            var ipHash = HashIpAddress(ipAddress);
            var ipKey = $"ip:{ipHash}";
            var ipAttempts = _attemptTracker.Increment(ipKey);

            // If same IP has too many failures across any user, lock the IP
            if (ipAttempts >= _config.MaxFailedLoginAttempts * 3) // 3x threshold for IP
            {
                var ipLock = LockIpForSuspiciousActivity(ipAddress,
                    $"Multiple failed login attempts ({ipAttempts})",
                    _config.FailedLoginLockMinutes);
                _attemptTracker.Reset(ipKey);

                return new LockCheckResult
                {
                    IsLocked = true,
                    LockType = ipLock.LockType,
                    Reason = ipLock.Reason,
                    ExpiresAt = ipLock.ExpiresAt,
                    RetryAfterSeconds = ipLock.ExpiresAt.HasValue
                        ? (int)Math.Ceiling((ipLock.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds)
                        : null,
                    Message = "Too many failed login attempts from this IP address"
                };
            }

            return LockCheckResult.NotLocked();
        }

        /// <inheritdoc />
        public LockCheckResult RecordFailedMfa(int userId, string username, string ipAddress)
        {
            var key = $"mfa:{userId}";
            var attempts = _attemptTracker.Increment(key);

            if (attempts >= _config.MaxFailedMfaAttempts)
            {
                var lockRecord = LockAccountForFailedMfa(userId, username, ipAddress, attempts);
                _attemptTracker.Reset(key);
                return LockCheckResult.Locked(lockRecord);
            }

            return LockCheckResult.NotLocked();
        }

        /// <inheritdoc />
        public void ResetFailedAttempts(int userId, string ipAddress)
        {
            _attemptTracker.Reset(userId.ToString());
            _attemptTracker.Reset($"mfa:{userId}");

            if (!string.IsNullOrEmpty(ipAddress))
            {
                var ipHash = HashIpAddress(ipAddress);
                _attemptTracker.Reset($"ip:{ipHash}");
            }
        }

        #endregion

        #region Private Methods

        private TimeSpan? CalculateProgressiveDuration(int userId, LockType lockType, TimeSpan? baseDuration)
        {
            if (!baseDuration.HasValue)
                return null;

            var recentLocks = _repository.GetRecentLocksCount(userId,
                TimeSpan.FromHours(_config.ProgressiveWindowHours), lockType);

            if (recentLocks == 0)
                return baseDuration;

            var multiplierIndex = Math.Min(recentLocks, _config.ProgressiveMultipliers.Count - 1);
            var multiplier = _config.ProgressiveMultipliers[multiplierIndex];

            return TimeSpan.FromMinutes(baseDuration.Value.TotalMinutes * multiplier);
        }

        private static string GetDefaultReason(LockType lockType)
        {
            return lockType switch
            {
                LockType.Manual => "Account locked by administrator",
                LockType.FailedLogin => "Too many failed login attempts",
                LockType.FailedMfa => "Too many failed MFA attempts",
                LockType.SuspiciousActivity => "Suspicious activity detected",
                LockType.RateLimitExceeded => "Rate limit exceeded",
                LockType.PasswordReset => "Password reset in progress",
                LockType.PolicyViolation => "Security policy violation",
                LockType.CompromiseDetected => "Possible account compromise detected",
                LockType.Inactivity => "Account locked due to inactivity",
                LockType.Maintenance => "System maintenance in progress",
                _ => "Account locked"
            };
        }

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

            if (ipAddress.Contains('.') && !ipAddress.Contains(':'))
            {
                var parts = ipAddress.Split('.');
                if (parts.Length == 4)
                    return $"{parts[0]}.{parts[1]}.{parts[2]}.***";
            }

            if (ipAddress.Contains(':'))
            {
                var parts = ipAddress.Split(':');
                if (parts.Length >= 4)
                    return string.Join(":", parts[..4]) + ":****:****:****:****";
            }

            return ipAddress;
        }

        #endregion
    }

    /// <summary>
    /// Failed attempt tracker interface
    /// </summary>
    public interface IFailedAttemptTracker
    {
        int Increment(string key);
        int GetCount(string key);
        void Reset(string key);
    }

    /// <summary>
    /// In-memory failed attempt tracker with auto-expiry
    /// </summary>
    public class InMemoryFailedAttemptTracker : IFailedAttemptTracker
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime FirstAttempt)> _attempts = new();
        private readonly TimeSpan _window;

        public InMemoryFailedAttemptTracker(TimeSpan? window = null)
        {
            _window = window ?? TimeSpan.FromMinutes(15);
        }

        public int Increment(string key)
        {
            var now = DateTime.UtcNow;

            return _attempts.AddOrUpdate(
                key,
                _ => (1, now),
                (_, existing) =>
                {
                    // Reset if outside window
                    if (now - existing.FirstAttempt > _window)
                        return (1, now);
                    return (existing.Count + 1, existing.FirstAttempt);
                }).Count;
        }

        public int GetCount(string key)
        {
            if (_attempts.TryGetValue(key, out var data))
            {
                if (DateTime.UtcNow - data.FirstAttempt <= _window)
                    return data.Count;
            }
            return 0;
        }

        public void Reset(string key)
        {
            _attempts.TryRemove(key, out _);
        }
    }
}
