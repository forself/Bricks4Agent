using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Bricks4Agent.Security.RateLimiting
{
    /// <summary>
    /// User session information
    /// </summary>
    public class UserSession
    {
        /// <summary>
        /// Session ID
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// User ID
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// IP address at login
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>
        /// User agent at login
        /// </summary>
        public string UserAgent { get; set; } = string.Empty;

        /// <summary>
        /// Device fingerprint
        /// </summary>
        public string Fingerprint { get; set; } = string.Empty;

        /// <summary>
        /// Session creation time
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Last activity time
        /// </summary>
        public DateTime LastActivityAt { get; set; }

        /// <summary>
        /// Session expiration time
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Whether session is active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Location info (if available)
        /// </summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>
        /// Device type
        /// </summary>
        public string DeviceType { get; set; } = string.Empty;

        /// <summary>
        /// Browser info
        /// </summary>
        public string Browser { get; set; } = string.Empty;

        /// <summary>
        /// Whether this is current session
        /// </summary>
        public bool IsCurrent { get; set; }
    }

    /// <summary>
    /// Login attempt record
    /// </summary>
    public class LoginAttempt
    {
        /// <summary>
        /// Attempted email/username
        /// </summary>
        public string Identifier { get; set; } = string.Empty;

        /// <summary>
        /// IP address
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>
        /// User agent
        /// </summary>
        public string UserAgent { get; set; } = string.Empty;

        /// <summary>
        /// Attempt timestamp
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Whether login was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Failure reason if not successful
        /// </summary>
        public string FailureReason { get; set; } = string.Empty;

        /// <summary>
        /// User ID if login was successful
        /// </summary>
        public int? UserId { get; set; }
    }

    /// <summary>
    /// User session management interface
    /// </summary>
    public interface IUserSessionService
    {
        /// <summary>
        /// Create a new session for user
        /// </summary>
        UserSession CreateSession(int userId, ClientConnectionInfo connectionInfo, TimeSpan duration);

        /// <summary>
        /// Get session by ID
        /// </summary>
        UserSession? GetSession(string sessionId);

        /// <summary>
        /// Get all active sessions for a user
        /// </summary>
        List<UserSession> GetUserSessions(int userId);

        /// <summary>
        /// Update session activity
        /// </summary>
        void UpdateActivity(string sessionId);

        /// <summary>
        /// Invalidate a session
        /// </summary>
        void InvalidateSession(string sessionId);

        /// <summary>
        /// Invalidate all sessions for a user
        /// </summary>
        void InvalidateAllSessions(int userId);

        /// <summary>
        /// Invalidate all sessions except current
        /// </summary>
        void InvalidateOtherSessions(int userId, string currentSessionId);

        /// <summary>
        /// Check if session is valid
        /// </summary>
        bool IsSessionValid(string sessionId);

        /// <summary>
        /// Record a login attempt
        /// </summary>
        void RecordLoginAttempt(LoginAttempt attempt);

        /// <summary>
        /// Get recent login attempts for analysis
        /// </summary>
        List<LoginAttempt> GetRecentLoginAttempts(string identifier, int count = 10);

        /// <summary>
        /// Get recent login attempts by IP
        /// </summary>
        List<LoginAttempt> GetRecentLoginAttemptsByIp(string ipAddress, int count = 10);

        /// <summary>
        /// Get user's login history
        /// </summary>
        List<LoginAttempt> GetUserLoginHistory(int userId, int count = 10);
    }

    /// <summary>
    /// In-memory user session service
    /// </summary>
    public class UserSessionService : IUserSessionService, IDisposable
    {
        private readonly ConcurrentDictionary<string, UserSession> _sessions = new();
        private readonly ConcurrentDictionary<int, HashSet<string>> _userSessions = new();
        private readonly ConcurrentQueue<LoginAttempt> _loginAttempts = new();
        private readonly int _maxLoginAttempts;
        private readonly Timer _cleanupTimer;
        private readonly object _lock = new object();
        private bool _disposed;

        public UserSessionService(int maxLoginAttempts = 10000)
        {
            _maxLoginAttempts = maxLoginAttempts;
            _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <inheritdoc />
        public UserSession CreateSession(int userId, ClientConnectionInfo connectionInfo, TimeSpan duration)
        {
            var sessionId = GenerateSessionId();
            var now = DateTime.UtcNow;

            var session = new UserSession
            {
                SessionId = sessionId,
                UserId = userId,
                IpAddress = connectionInfo?.IpAddress ?? string.Empty,
                UserAgent = connectionInfo?.UserAgent ?? string.Empty,
                Fingerprint = connectionInfo?.Fingerprint ?? string.Empty,
                CreatedAt = now,
                LastActivityAt = now,
                ExpiresAt = now.Add(duration),
                IsActive = true,
                DeviceType = connectionInfo?.UserAgentInfo?.DeviceType ?? string.Empty,
                Browser = connectionInfo?.UserAgentInfo?.Browser ?? string.Empty
            };

            _sessions[sessionId] = session;

            // Track by user ID
            lock (_lock)
            {
                if (!_userSessions.TryGetValue(userId, out var sessions))
                {
                    sessions = new HashSet<string>();
                    _userSessions[userId] = sessions;
                }
                sessions.Add(sessionId);
            }

            return session;
        }

        /// <inheritdoc />
        public UserSession? GetSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            if (_sessions.TryGetValue(sessionId, out var session))
            {
                if (session.IsActive && session.ExpiresAt > DateTime.UtcNow)
                {
                    return session;
                }
            }

            return null;
        }

        /// <inheritdoc />
        public List<UserSession> GetUserSessions(int userId)
        {
            var result = new List<UserSession>();

            lock (_lock)
            {
                if (_userSessions.TryGetValue(userId, out var sessionIds))
                {
                    foreach (var id in sessionIds.ToList())
                    {
                        if (_sessions.TryGetValue(id, out var session) &&
                            session.IsActive &&
                            session.ExpiresAt > DateTime.UtcNow)
                        {
                            result.Add(session);
                        }
                    }
                }
            }

            return result.OrderByDescending(s => s.LastActivityAt).ToList();
        }

        /// <inheritdoc />
        public void UpdateActivity(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.LastActivityAt = DateTime.UtcNow;
            }
        }

        /// <inheritdoc />
        public void InvalidateSession(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.IsActive = false;
            }
        }

        /// <inheritdoc />
        public void InvalidateAllSessions(int userId)
        {
            lock (_lock)
            {
                if (_userSessions.TryGetValue(userId, out var sessionIds))
                {
                    foreach (var id in sessionIds)
                    {
                        if (_sessions.TryGetValue(id, out var session))
                        {
                            session.IsActive = false;
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        public void InvalidateOtherSessions(int userId, string currentSessionId)
        {
            lock (_lock)
            {
                if (_userSessions.TryGetValue(userId, out var sessionIds))
                {
                    foreach (var id in sessionIds)
                    {
                        if (id != currentSessionId && _sessions.TryGetValue(id, out var session))
                        {
                            session.IsActive = false;
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        public bool IsSessionValid(string sessionId)
        {
            return GetSession(sessionId) != null;
        }

        /// <inheritdoc />
        public void RecordLoginAttempt(LoginAttempt attempt)
        {
            if (attempt == null)
                return;

            attempt.Timestamp = DateTime.UtcNow;
            _loginAttempts.Enqueue(attempt);

            // Limit queue size
            while (_loginAttempts.Count > _maxLoginAttempts)
            {
                _loginAttempts.TryDequeue(out _);
            }
        }

        /// <inheritdoc />
        public List<LoginAttempt> GetRecentLoginAttempts(string identifier, int count = 10)
        {
            if (string.IsNullOrEmpty(identifier))
                return new List<LoginAttempt>();

            return _loginAttempts
                .Where(a => a.Identifier?.Equals(identifier, StringComparison.OrdinalIgnoreCase) == true)
                .OrderByDescending(a => a.Timestamp)
                .Take(count)
                .ToList();
        }

        /// <inheritdoc />
        public List<LoginAttempt> GetRecentLoginAttemptsByIp(string ipAddress, int count = 10)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return new List<LoginAttempt>();

            return _loginAttempts
                .Where(a => a.IpAddress == ipAddress)
                .OrderByDescending(a => a.Timestamp)
                .Take(count)
                .ToList();
        }

        /// <inheritdoc />
        public List<LoginAttempt> GetUserLoginHistory(int userId, int count = 10)
        {
            return _loginAttempts
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.Timestamp)
                .Take(count)
                .ToList();
        }

        private static string GenerateSessionId()
        {
            var bytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private void Cleanup(object? state)
        {
            var now = DateTime.UtcNow;
            var cutoff = now.AddHours(-24); // Keep login attempts for 24 hours

            // Clean expired sessions
            var expiredSessions = _sessions
                .Where(kv => !kv.Value.IsActive || kv.Value.ExpiresAt < now)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var id in expiredSessions)
            {
                if (_sessions.TryRemove(id, out var session))
                {
                    lock (_lock)
                    {
                        if (_userSessions.TryGetValue(session.UserId, out var sessions))
                        {
                            sessions.Remove(id);
                        }
                    }
                }
            }

            // Clean old login attempts (keep last 24 hours)
            while (_loginAttempts.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            {
                _loginAttempts.TryDequeue(out _);
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
    }
}
