using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Bricks4Agent.Security.RateLimiting
{
    /// <summary>
    /// Rate limit check result
    /// </summary>
    public class RateLimitResult
    {
        /// <summary>
        /// Whether the request is allowed
        /// </summary>
        public bool IsAllowed { get; set; }

        /// <summary>
        /// Current request count in the window
        /// </summary>
        public int CurrentCount { get; set; }

        /// <summary>
        /// Maximum allowed requests
        /// </summary>
        public int Limit { get; set; }

        /// <summary>
        /// Remaining requests in the window
        /// </summary>
        public int Remaining => Math.Max(0, Limit - CurrentCount);

        /// <summary>
        /// When the current window resets
        /// </summary>
        public DateTime WindowReset { get; set; }

        /// <summary>
        /// Seconds until window resets
        /// </summary>
        public int RetryAfterSeconds => (int)Math.Ceiling((WindowReset - DateTime.UtcNow).TotalSeconds);

        /// <summary>
        /// Rule name that was applied
        /// </summary>
        public string RuleName { get; set; }

        /// <summary>
        /// Client identifier (IP, user ID, etc.)
        /// </summary>
        public string ClientId { get; set; }

        public static RateLimitResult Allowed(string ruleName, string clientId, int currentCount, int limit, DateTime windowReset)
        {
            return new RateLimitResult
            {
                IsAllowed = true,
                RuleName = ruleName,
                ClientId = clientId,
                CurrentCount = currentCount,
                Limit = limit,
                WindowReset = windowReset
            };
        }

        public static RateLimitResult Denied(string ruleName, string clientId, int currentCount, int limit, DateTime windowReset)
        {
            return new RateLimitResult
            {
                IsAllowed = false,
                RuleName = ruleName,
                ClientId = clientId,
                CurrentCount = currentCount,
                Limit = limit,
                WindowReset = windowReset
            };
        }
    }

    /// <summary>
    /// Rate limiting rule
    /// </summary>
    public class RateLimitRule
    {
        /// <summary>
        /// Rule name for identification
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Maximum requests allowed in the window
        /// </summary>
        public int Limit { get; set; }

        /// <summary>
        /// Time window for the limit
        /// </summary>
        public TimeSpan Window { get; set; }

        /// <summary>
        /// Lockout duration when limit is exceeded (optional)
        /// </summary>
        public TimeSpan? LockoutDuration { get; set; }

        /// <summary>
        /// Whether to apply stricter limits for suspicious IPs
        /// </summary>
        public bool StrictModeForSuspicious { get; set; } = true;

        /// <summary>
        /// Limit multiplier for suspicious IPs (e.g., 0.5 = half the limit)
        /// </summary>
        public double SuspiciousLimitMultiplier { get; set; } = 0.5;
    }

    /// <summary>
    /// IP-based rate limiter interface
    /// </summary>
    public interface IIpRateLimiter
    {
        /// <summary>
        /// Check if request is allowed and increment counter
        /// </summary>
        RateLimitResult CheckAndIncrement(string clientId, string ruleName);

        /// <summary>
        /// Check if request would be allowed (without incrementing)
        /// </summary>
        RateLimitResult Check(string clientId, string ruleName);

        /// <summary>
        /// Reset counter for a specific client and rule
        /// </summary>
        void Reset(string clientId, string ruleName);

        /// <summary>
        /// Add IP to suspicious list
        /// </summary>
        void MarkSuspicious(string ip, TimeSpan duration, string reason);

        /// <summary>
        /// Check if IP is suspicious
        /// </summary>
        bool IsSuspicious(string ip);

        /// <summary>
        /// Block an IP permanently (until manually unblocked)
        /// </summary>
        void BlockIp(string ip, string reason);

        /// <summary>
        /// Unblock an IP
        /// </summary>
        void UnblockIp(string ip);

        /// <summary>
        /// Check if IP is blocked
        /// </summary>
        bool IsBlocked(string ip);

        /// <summary>
        /// Get statistics for an IP
        /// </summary>
        IpStatistics GetStatistics(string ip);
    }

    /// <summary>
    /// IP statistics
    /// </summary>
    public class IpStatistics
    {
        public string IpAddress { get; set; }
        public Dictionary<string, int> RequestCounts { get; set; } = new();
        public int TotalRequests { get; set; }
        public int BlockedRequests { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsSuspicious { get; set; }
        public bool IsBlocked { get; set; }
        public string BlockReason { get; set; }
    }

    /// <summary>
    /// In-memory IP rate limiter using sliding window algorithm
    /// </summary>
    public class IpRateLimiter : IIpRateLimiter, IDisposable
    {
        private readonly ConcurrentDictionary<string, RateLimitRule> _rules = new();
        private readonly ConcurrentDictionary<string, SlidingWindowCounter> _counters = new();
        private readonly ConcurrentDictionary<string, DateTime> _lockouts = new();
        private readonly ConcurrentDictionary<string, (DateTime ExpiresAt, string Reason)> _suspiciousIps = new();
        private readonly ConcurrentDictionary<string, string> _blockedIps = new();
        private readonly ConcurrentDictionary<string, IpStats> _stats = new();
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        /// <summary>
        /// Create rate limiter with default rules
        /// </summary>
        public IpRateLimiter()
        {
            // Default rules
            AddRule(new RateLimitRule
            {
                Name = "login",
                Limit = 5,
                Window = TimeSpan.FromMinutes(1),
                LockoutDuration = TimeSpan.FromMinutes(15)
            });

            AddRule(new RateLimitRule
            {
                Name = "register",
                Limit = 3,
                Window = TimeSpan.FromMinutes(10),
                LockoutDuration = TimeSpan.FromHours(1)
            });

            AddRule(new RateLimitRule
            {
                Name = "api",
                Limit = 100,
                Window = TimeSpan.FromMinutes(1)
            });

            AddRule(new RateLimitRule
            {
                Name = "password_reset",
                Limit = 3,
                Window = TimeSpan.FromHours(1),
                LockoutDuration = TimeSpan.FromHours(24)
            });

            // Cleanup timer (every 5 minutes)
            _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Add or update a rate limit rule
        /// </summary>
        public void AddRule(RateLimitRule rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));
            if (string.IsNullOrEmpty(rule.Name))
                throw new ArgumentException("Rule name is required", nameof(rule));

            _rules[rule.Name] = rule;
        }

        /// <inheritdoc />
        public RateLimitResult CheckAndIncrement(string clientId, string ruleName)
        {
            return CheckInternal(clientId, ruleName, increment: true);
        }

        /// <inheritdoc />
        public RateLimitResult Check(string clientId, string ruleName)
        {
            return CheckInternal(clientId, ruleName, increment: false);
        }

        private RateLimitResult CheckInternal(string clientId, string ruleName, bool increment)
        {
            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentNullException(nameof(clientId));

            // Check if blocked
            if (_blockedIps.ContainsKey(clientId))
            {
                UpdateStats(clientId, ruleName, blocked: true);
                return RateLimitResult.Denied(ruleName, clientId, 0, 0, DateTime.MaxValue);
            }

            // Get rule
            if (!_rules.TryGetValue(ruleName, out var rule))
            {
                // No rule = allowed
                return RateLimitResult.Allowed(ruleName, clientId, 0, int.MaxValue, DateTime.MaxValue);
            }

            // Check lockout
            var lockoutKey = $"{clientId}:{ruleName}";
            if (_lockouts.TryGetValue(lockoutKey, out var lockoutUntil) && lockoutUntil > DateTime.UtcNow)
            {
                UpdateStats(clientId, ruleName, blocked: true);
                return RateLimitResult.Denied(ruleName, clientId, rule.Limit, rule.Limit, lockoutUntil);
            }

            // Apply suspicious IP multiplier
            var effectiveLimit = rule.Limit;
            if (rule.StrictModeForSuspicious && IsSuspicious(clientId))
            {
                effectiveLimit = (int)(rule.Limit * rule.SuspiciousLimitMultiplier);
            }

            // Get or create counter
            var counterKey = $"{clientId}:{ruleName}";
            var counter = _counters.GetOrAdd(counterKey, _ => new SlidingWindowCounter(rule.Window));

            // Get current count
            var currentCount = counter.GetCount();
            var windowReset = counter.GetWindowReset();

            // Increment if requested
            if (increment)
            {
                currentCount = counter.Increment();
                UpdateStats(clientId, ruleName, blocked: false);
            }

            // Check limit
            if (currentCount > effectiveLimit)
            {
                // Apply lockout if configured
                if (rule.LockoutDuration.HasValue)
                {
                    _lockouts[lockoutKey] = DateTime.UtcNow.Add(rule.LockoutDuration.Value);
                    windowReset = DateTime.UtcNow.Add(rule.LockoutDuration.Value);
                }

                return RateLimitResult.Denied(ruleName, clientId, currentCount, effectiveLimit, windowReset);
            }

            return RateLimitResult.Allowed(ruleName, clientId, currentCount, effectiveLimit, windowReset);
        }

        /// <inheritdoc />
        public void Reset(string clientId, string ruleName)
        {
            var counterKey = $"{clientId}:{ruleName}";
            _counters.TryRemove(counterKey, out _);

            var lockoutKey = $"{clientId}:{ruleName}";
            _lockouts.TryRemove(lockoutKey, out _);
        }

        /// <inheritdoc />
        public void MarkSuspicious(string ip, TimeSpan duration, string reason)
        {
            if (string.IsNullOrEmpty(ip))
                return;

            _suspiciousIps[ip] = (DateTime.UtcNow.Add(duration), reason);
        }

        /// <inheritdoc />
        public bool IsSuspicious(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return false;

            if (_suspiciousIps.TryGetValue(ip, out var entry))
            {
                if (entry.ExpiresAt > DateTime.UtcNow)
                    return true;

                // Expired, remove
                _suspiciousIps.TryRemove(ip, out _);
            }

            return false;
        }

        /// <inheritdoc />
        public void BlockIp(string ip, string reason)
        {
            if (!string.IsNullOrEmpty(ip))
            {
                _blockedIps[ip] = reason ?? "Blocked";
            }
        }

        /// <inheritdoc />
        public void UnblockIp(string ip)
        {
            if (!string.IsNullOrEmpty(ip))
            {
                _blockedIps.TryRemove(ip, out _);
            }
        }

        /// <inheritdoc />
        public bool IsBlocked(string ip)
        {
            return !string.IsNullOrEmpty(ip) && _blockedIps.ContainsKey(ip);
        }

        /// <inheritdoc />
        public IpStatistics GetStatistics(string ip)
        {
            var stats = _stats.GetOrAdd(ip, _ => new IpStats { FirstSeen = DateTime.UtcNow });

            var result = new IpStatistics
            {
                IpAddress = ip,
                TotalRequests = stats.TotalRequests,
                BlockedRequests = stats.BlockedRequests,
                FirstSeen = stats.FirstSeen,
                LastSeen = stats.LastSeen,
                RequestCounts = new Dictionary<string, int>(stats.RuleCounts),
                IsSuspicious = IsSuspicious(ip),
                IsBlocked = IsBlocked(ip)
            };

            if (_blockedIps.TryGetValue(ip, out var reason))
            {
                result.BlockReason = reason;
            }

            return result;
        }

        private void UpdateStats(string ip, string ruleName, bool blocked)
        {
            var stats = _stats.GetOrAdd(ip, _ => new IpStats { FirstSeen = DateTime.UtcNow });

            Interlocked.Increment(ref stats.TotalRequests);
            if (blocked)
            {
                Interlocked.Increment(ref stats.BlockedRequests);
            }

            stats.LastSeen = DateTime.UtcNow;
            stats.RuleCounts.AddOrUpdate(ruleName, 1, (_, count) => count + 1);
        }

        private void Cleanup(object state)
        {
            var now = DateTime.UtcNow;

            // Clean expired lockouts
            var expiredLockouts = _lockouts
                .Where(kv => kv.Value < now)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expiredLockouts)
            {
                _lockouts.TryRemove(key, out _);
            }

            // Clean expired suspicious IPs
            var expiredSuspicious = _suspiciousIps
                .Where(kv => kv.Value.ExpiresAt < now)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expiredSuspicious)
            {
                _suspiciousIps.TryRemove(key, out _);
            }

            // Clean old counters (not accessed for 1 hour)
            var oldCounters = _counters
                .Where(kv => kv.Value.LastAccess < now.AddHours(-1))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in oldCounters)
            {
                _counters.TryRemove(key, out _);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cleanupTimer?.Dispose();
                _disposed = true;
            }
        }

        #region Internal Classes

        private class IpStats
        {
            public int TotalRequests;
            public int BlockedRequests;
            public DateTime FirstSeen;
            public DateTime LastSeen;
            public ConcurrentDictionary<string, int> RuleCounts = new();
        }

        /// <summary>
        /// Sliding window counter for rate limiting
        /// </summary>
        private class SlidingWindowCounter
        {
            private readonly TimeSpan _window;
            private readonly ConcurrentQueue<DateTime> _timestamps = new();
            private readonly object _lock = new object();
            public DateTime LastAccess { get; private set; } = DateTime.UtcNow;

            public SlidingWindowCounter(TimeSpan window)
            {
                _window = window;
            }

            public int Increment()
            {
                lock (_lock)
                {
                    CleanupOld();
                    _timestamps.Enqueue(DateTime.UtcNow);
                    LastAccess = DateTime.UtcNow;
                    return _timestamps.Count;
                }
            }

            public int GetCount()
            {
                lock (_lock)
                {
                    CleanupOld();
                    LastAccess = DateTime.UtcNow;
                    return _timestamps.Count;
                }
            }

            public DateTime GetWindowReset()
            {
                if (_timestamps.TryPeek(out var oldest))
                {
                    return oldest.Add(_window);
                }
                return DateTime.UtcNow.Add(_window);
            }

            private void CleanupOld()
            {
                var cutoff = DateTime.UtcNow.Subtract(_window);
                while (_timestamps.TryPeek(out var oldest) && oldest < cutoff)
                {
                    _timestamps.TryDequeue(out _);
                }
            }
        }

        #endregion
    }
}
