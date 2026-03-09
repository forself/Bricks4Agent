using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Bricks4Agent.Security.Mfa.Models;

namespace Bricks4Agent.Security.Mfa
{
    /// <summary>
    /// In-memory MFA repository for development and testing
    /// In production, replace with a database-backed implementation
    /// </summary>
    public class InMemoryMfaRepository : IMfaRepository
    {
        private readonly ConcurrentDictionary<int, UserMfaConfig> _mfaConfigs = new();
        private readonly ConcurrentDictionary<int, List<MfaRecoveryCode>> _recoveryCodes = new();
        private readonly ConcurrentDictionary<int, List<MfaOtpCode>> _otpCodes = new();
        private int _recoveryCodeIdCounter = 0;
        private int _otpCodeIdCounter = 0;

        /// <inheritdoc />
        public UserMfaConfig GetUserMfaConfig(int userId)
        {
            _mfaConfigs.TryGetValue(userId, out var config);
            return config;
        }

        /// <inheritdoc />
        public void SaveUserMfaConfig(UserMfaConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            _mfaConfigs.AddOrUpdate(config.UserId, config, (_, _) => config);
        }

        /// <inheritdoc />
        public void DeleteRecoveryCodes(int userId)
        {
            _recoveryCodes.TryRemove(userId, out _);
        }

        /// <inheritdoc />
        public void SaveRecoveryCode(MfaRecoveryCode code)
        {
            if (code == null)
                throw new ArgumentNullException(nameof(code));

            code.Id = System.Threading.Interlocked.Increment(ref _recoveryCodeIdCounter);

            var codes = _recoveryCodes.GetOrAdd(code.UserId, _ => new List<MfaRecoveryCode>());
            lock (codes)
            {
                codes.Add(code);
            }
        }

        /// <inheritdoc />
        public MfaRecoveryCode GetRecoveryCode(int userId, string codeHash)
        {
            if (!_recoveryCodes.TryGetValue(userId, out var codes))
                return null;

            lock (codes)
            {
                return codes.FirstOrDefault(c => c.CodeHash == codeHash && !c.IsUsed);
            }
        }

        /// <inheritdoc />
        public void MarkRecoveryCodeUsed(MfaRecoveryCode code)
        {
            if (code == null)
                return;

            if (_recoveryCodes.TryGetValue(code.UserId, out var codes))
            {
                lock (codes)
                {
                    var existing = codes.FirstOrDefault(c => c.Id == code.Id);
                    if (existing != null)
                    {
                        existing.IsUsed = true;
                        existing.UsedAt = DateTime.UtcNow;
                    }
                }
            }
        }

        /// <inheritdoc />
        public void SaveOtpCode(MfaOtpCode code)
        {
            if (code == null)
                throw new ArgumentNullException(nameof(code));

            code.Id = System.Threading.Interlocked.Increment(ref _otpCodeIdCounter);

            var codes = _otpCodes.GetOrAdd(code.UserId, _ => new List<MfaOtpCode>());
            lock (codes)
            {
                // Remove expired codes
                codes.RemoveAll(c => c.ExpiresAt < DateTime.UtcNow || c.IsUsed);
                codes.Add(code);
            }
        }

        /// <inheritdoc />
        public MfaOtpCode GetValidOtpCode(int userId, string codeHash, MfaMethod method)
        {
            if (!_otpCodes.TryGetValue(userId, out var codes))
                return null;

            lock (codes)
            {
                return codes.FirstOrDefault(c =>
                    c.CodeHash == codeHash &&
                    c.Method == method &&
                    !c.IsUsed &&
                    c.ExpiresAt > DateTime.UtcNow);
            }
        }

        /// <inheritdoc />
        public void MarkOtpCodeUsed(MfaOtpCode code)
        {
            if (code == null)
                return;

            if (_otpCodes.TryGetValue(code.UserId, out var codes))
            {
                lock (codes)
                {
                    var existing = codes.FirstOrDefault(c => c.Id == code.Id);
                    if (existing != null)
                    {
                        existing.IsUsed = true;
                    }
                }
            }
        }

        /// <summary>
        /// Clear all data (for testing)
        /// </summary>
        public void Clear()
        {
            _mfaConfigs.Clear();
            _recoveryCodes.Clear();
            _otpCodes.Clear();
        }
    }

    /// <summary>
    /// In-memory user repository for development and testing
    /// In production, replace with a database-backed implementation
    /// </summary>
    public class InMemoryUserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<int, UserModel> _users = new();
        private readonly ConcurrentDictionary<string, int> _emailIndex = new();
        private int _userIdCounter = 0;

        /// <inheritdoc />
        public UserModel GetUserById(int id)
        {
            _users.TryGetValue(id, out var user);
            return user;
        }

        /// <inheritdoc />
        public UserModel GetUserByEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
                return null;

            email = email.ToLowerInvariant();
            if (_emailIndex.TryGetValue(email, out var userId))
            {
                return GetUserById(userId);
            }
            return null;
        }

        /// <inheritdoc />
        public bool EmailExists(string email)
        {
            if (string.IsNullOrEmpty(email))
                return false;

            return _emailIndex.ContainsKey(email.ToLowerInvariant());
        }

        /// <inheritdoc />
        public int CreateUser(UserCreateModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var userId = System.Threading.Interlocked.Increment(ref _userIdCounter);
            var email = model.Email.ToLowerInvariant();

            var user = new UserModel
            {
                Id = userId,
                Name = model.Name,
                Email = email,
                PasswordHash = model.PasswordHash,
                Role = model.Role,
                Status = model.Status
            };

            _users[userId] = user;
            _emailIndex[email] = userId;

            return userId;
        }

        /// <inheritdoc />
        public void UpdateLastLogin(int userId)
        {
            // In a real implementation, update the last login timestamp in DB
        }

        /// <summary>
        /// Clear all data (for testing)
        /// </summary>
        public void Clear()
        {
            _users.Clear();
            _emailIndex.Clear();
            _userIdCounter = 0;
        }
    }

    /// <summary>
    /// Console email service for development and testing
    /// In production, replace with SMTP or email service implementation
    /// </summary>
    public class ConsoleEmailService : IEmailService
    {
        /// <inheritdoc />
        public bool SendEmail(string to, string subject, string body)
        {
            Console.WriteLine("=== EMAIL ===");
            Console.WriteLine($"To: {to}");
            Console.WriteLine($"Subject: {subject}");
            Console.WriteLine($"Body:\n{body}");
            Console.WriteLine("=============");
            return true;
        }
    }
}
