using System;
using System.Security.Cryptography;
using System.Text;

namespace Bricks4Agent.Security.Encryption
{
    /// <summary>
    /// Password hashing utility using BCrypt algorithm
    /// </summary>
    public class PasswordHasher
    {
        private const int WorkFactor = 12; // BCrypt work factor (2^12 iterations)

        /// <summary>
        /// Hash a password using BCrypt
        /// </summary>
        /// <param name="password">Plain text password</param>
        /// <returns>Hashed password</returns>
        public string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentNullException(nameof(password), "Password cannot be null or empty");

            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        /// <summary>
        /// Verify a password against a hash
        /// </summary>
        /// <param name="password">Plain text password</param>
        /// <param name="hashedPassword">Hashed password</param>
        /// <returns>True if password matches, false otherwise</returns>
        public bool VerifyPassword(string password, string hashedPassword)
        {
            if (string.IsNullOrEmpty(password))
                return false;

            if (string.IsNullOrEmpty(hashedPassword))
                return false;

            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a password hash needs to be rehashed (work factor changed)
        /// </summary>
        /// <param name="hashedPassword">Hashed password</param>
        /// <returns>True if rehashing is needed</returns>
        public bool NeedsRehash(string hashedPassword)
        {
            if (string.IsNullOrEmpty(hashedPassword))
                return true;

            try
            {
                // Extract work factor from hash
                var parts = hashedPassword.Split('$');
                if (parts.Length >= 3)
                {
                    var currentWorkFactor = int.Parse(parts[2]);
                    return currentWorkFactor < WorkFactor;
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Generate a random salt for additional security
        /// </summary>
        /// <returns>Random salt string</returns>
        public string GenerateSalt()
        {
            return BCrypt.Net.BCrypt.GenerateSalt(WorkFactor);
        }

        /// <summary>
        /// Hash password with custom salt
        /// </summary>
        /// <param name="password">Plain text password</param>
        /// <param name="salt">Custom salt</param>
        /// <returns>Hashed password</returns>
        public string HashPasswordWithSalt(string password, string salt)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentNullException(nameof(password), "Password cannot be null or empty");

            if (string.IsNullOrEmpty(salt))
                throw new ArgumentNullException(nameof(salt), "Salt cannot be null or empty");

            return BCrypt.Net.BCrypt.HashPassword(password, salt);
        }

        /// <summary>
        /// Validate password strength
        /// </summary>
        /// <param name="password">Password to validate</param>
        /// <returns>Validation result</returns>
        public PasswordStrengthResult ValidatePasswordStrength(string password)
        {
            var result = new PasswordStrengthResult();

            if (string.IsNullOrEmpty(password))
            {
                result.IsValid = false;
                result.Errors.Add("Password is required");
                return result;
            }

            // Check length
            if (password.Length < 8)
            {
                result.IsValid = false;
                result.Errors.Add("Password must be at least 8 characters long");
            }

            // Use char-based checks instead of Regex for simple character class validation
            // This avoids potential ReDoS attacks and is more performant

            // Check for uppercase
            if (!password.Any(char.IsUpper))
            {
                result.IsValid = false;
                result.Errors.Add("Password must contain at least one uppercase letter");
            }

            // Check for lowercase
            if (!password.Any(char.IsLower))
            {
                result.IsValid = false;
                result.Errors.Add("Password must contain at least one lowercase letter");
            }

            // Check for digit
            if (!password.Any(char.IsDigit))
            {
                result.IsValid = false;
                result.Errors.Add("Password must contain at least one digit");
            }

            // Check for special character
            if (!password.Any(c => !char.IsLetterOrDigit(c)))
            {
                result.IsValid = false;
                result.Errors.Add("Password must contain at least one special character");
            }

            // Check for common passwords
            var commonPasswords = new[] { "password", "123456", "12345678", "qwerty", "abc123" };
            if (Array.Exists(commonPasswords, p => password.ToLower().Contains(p)))
            {
                result.IsValid = false;
                result.Errors.Add("Password is too common");
            }

            return result;
        }

        /// <summary>
        /// Calculate password strength score (0-5)
        /// </summary>
        /// <param name="password">Password to evaluate</param>
        /// <returns>Strength score (0=very weak, 5=very strong)</returns>
        public int CalculatePasswordStrength(string password)
        {
            if (string.IsNullOrEmpty(password))
                return 0;

            // Limit password length to prevent ReDoS
            var checkPassword = password.Length > 256 ? password.Substring(0, 256) : password;

            int score = 0;

            // Length
            if (password.Length >= 8) score++;
            if (password.Length >= 12) score++;

            // Character variety - use char checks instead of Regex for performance and security
            if (checkPassword.Any(char.IsUpper)) score++;
            if (checkPassword.Any(char.IsLower)) score++;
            if (checkPassword.Any(char.IsDigit)) score++;
            if (checkPassword.Any(c => !char.IsLetterOrDigit(c))) score++;

            // Penalty for common patterns - with timeout protection
            var regexTimeout = TimeSpan.FromMilliseconds(100);
            try
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(checkPassword, @"(.)\1{2,}",
                    System.Text.RegularExpressions.RegexOptions.None, regexTimeout))
                    score--; // Repeating characters
                if (System.Text.RegularExpressions.Regex.IsMatch(checkPassword, @"(012|123|234|345|456|567|678|789|890)",
                    System.Text.RegularExpressions.RegexOptions.None, regexTimeout))
                    score--; // Sequential numbers
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                // Treat as suspicious pattern if regex times out
                score--;
            }

            return Math.Max(0, Math.Min(5, score));
        }

        /// <summary>
        /// Generate a random password
        /// </summary>
        /// <param name="length">Password length (default: 16)</param>
        /// <param name="includeSpecialChars">Include special characters</param>
        /// <returns>Random password</returns>
        public string GenerateRandomPassword(int length = 16, bool includeSpecialChars = true)
        {
            const string lowercase = "abcdefghijklmnopqrstuvwxyz";
            const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string digits = "0123456789";
            const string special = "!@#$%^&*()_+-=[]{}|;:,.<>?";

            var charSet = lowercase + uppercase + digits;
            if (includeSpecialChars)
                charSet += special;

            var password = new StringBuilder();
            using (var rng = RandomNumberGenerator.Create())
            {
                var buffer = new byte[length];
                rng.GetBytes(buffer);

                foreach (var b in buffer)
                {
                    password.Append(charSet[b % charSet.Length]);
                }
            }

            // Ensure at least one of each required type - use char checks instead of Regex
            var result = password.ToString();
            if (!result.Any(char.IsUpper))
                result = result.Substring(0, result.Length - 1) + uppercase[RandomNumberGenerator.GetInt32(uppercase.Length)];
            if (!result.Any(char.IsLower))
                result = result.Substring(0, result.Length - 1) + lowercase[RandomNumberGenerator.GetInt32(lowercase.Length)];
            if (!result.Any(char.IsDigit))
                result = result.Substring(0, result.Length - 1) + digits[RandomNumberGenerator.GetInt32(digits.Length)];

            return result;
        }
    }

    /// <summary>
    /// Password strength validation result
    /// </summary>
    public class PasswordStrengthResult
    {
        /// <summary>
        /// Whether password meets strength requirements
        /// </summary>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// List of validation errors
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();
    }
}
