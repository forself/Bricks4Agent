using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Bricks4Agent.Utils.String
{
    /// <summary>
    /// String extension methods and utilities
    /// </summary>
    public static class StringExtensions
    {
        // Regex timeout to prevent ReDoS attacks
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

        #region Null/Empty Checks

        /// <summary>
        /// Check if string is null or empty
        /// </summary>
        public static bool IsNullOrEmpty(this string str)
        {
            return string.IsNullOrEmpty(str);
        }

        /// <summary>
        /// Check if string is null, empty, or whitespace
        /// </summary>
        public static bool IsNullOrWhiteSpace(this string str)
        {
            return string.IsNullOrWhiteSpace(str);
        }

        /// <summary>
        /// Check if string has value (not null, empty, or whitespace)
        /// </summary>
        public static bool HasValue(this string str)
        {
            return !string.IsNullOrWhiteSpace(str);
        }

        /// <summary>
        /// Return string or default value if null/empty
        /// </summary>
        public static string OrDefault(this string str, string defaultValue = "")
        {
            return string.IsNullOrWhiteSpace(str) ? defaultValue : str;
        }

        #endregion

        #region Case Conversion

        /// <summary>
        /// Convert to Title Case
        /// </summary>
        public static string ToTitleCase(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str.ToLower());
        }

        /// <summary>
        /// Convert to camelCase
        /// </summary>
        public static string ToCamelCase(this string str)
        {
            if (string.IsNullOrWhiteSpace(str) || str.Length == 0)
                return str;

            return char.ToLowerInvariant(str[0]) + str.Substring(1);
        }

        /// <summary>
        /// Convert to PascalCase
        /// </summary>
        public static string ToPascalCase(this string str)
        {
            if (string.IsNullOrWhiteSpace(str) || str.Length == 0)
                return str;

            return char.ToUpperInvariant(str[0]) + str.Substring(1);
        }

        /// <summary>
        /// Convert to snake_case
        /// </summary>
        public static string ToSnakeCase(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            var result = Regex.Replace(str, @"([a-z0-9])([A-Z])", "$1_$2", RegexOptions.None, RegexTimeout);
            return result.ToLower();
        }

        /// <summary>
        /// Convert to kebab-case
        /// </summary>
        public static string ToKebabCase(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            var result = Regex.Replace(str, @"([a-z0-9])([A-Z])", "$1-$2", RegexOptions.None, RegexTimeout);
            return result.ToLower();
        }

        #endregion

        #region Trimming

        /// <summary>
        /// Trim and remove extra whitespace
        /// </summary>
        public static string TrimAndReduce(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            return Regex.Replace(str.Trim(), @"\s+", " ", RegexOptions.None, RegexTimeout);
        }

        /// <summary>
        /// Remove all whitespace
        /// </summary>
        public static string RemoveWhitespace(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            return Regex.Replace(str, @"\s+", "", RegexOptions.None, RegexTimeout);
        }

        #endregion

        #region Truncation

        /// <summary>
        /// Truncate string to max length
        /// </summary>
        public static string Truncate(this string str, int maxLength, string suffix = "...")
        {
            if (string.IsNullOrWhiteSpace(str) || str.Length <= maxLength)
                return str;

            return str.Substring(0, maxLength) + suffix;
        }

        /// <summary>
        /// Truncate at word boundary
        /// </summary>
        public static string TruncateAtWord(this string str, int maxLength, string suffix = "...")
        {
            if (string.IsNullOrWhiteSpace(str) || str.Length <= maxLength)
                return str;

            var truncated = str.Substring(0, maxLength);
            var lastSpace = truncated.LastIndexOf(' ');

            if (lastSpace > 0)
                truncated = truncated.Substring(0, lastSpace);

            return truncated + suffix;
        }

        #endregion

        #region Masking

        /// <summary>
        /// Mask string (for sensitive data)
        /// </summary>
        public static string Mask(this string str, int visibleChars = 4, char maskChar = '*')
        {
            if (string.IsNullOrWhiteSpace(str) || str.Length <= visibleChars)
                return str;

            var visible = str.Substring(str.Length - visibleChars);
            var masked = new string(maskChar, str.Length - visibleChars);

            return masked + visible;
        }

        /// <summary>
        /// Mask email address
        /// </summary>
        public static string MaskEmail(this string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
                return email;

            var parts = email.Split('@');
            var localPart = parts[0];
            var domain = parts[1];

            var maskedLocal = localPart.Length > 2
                ? localPart[0] + new string('*', localPart.Length - 2) + localPart[localPart.Length - 1]
                : localPart;

            return $"{maskedLocal}@{domain}";
        }

        #endregion

        #region Validation

        /// <summary>
        /// Check if string is a valid email
        /// </summary>
        public static bool IsValidEmail(this string str)
        {
            if (string.IsNullOrWhiteSpace(str) || str.Length > 254)
                return false;

            var pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            return Regex.IsMatch(str, pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }

        /// <summary>
        /// Check if string is a valid URL
        /// </summary>
        public static bool IsValidUrl(this string str)
        {
            return Uri.TryCreate(str, UriKind.Absolute, out var uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Check if string contains only digits
        /// </summary>
        public static bool IsNumeric(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return false;

            return str.All(char.IsDigit);
        }

        /// <summary>
        /// Check if string contains only letters
        /// </summary>
        public static bool IsAlphabetic(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return false;

            return str.All(char.IsLetter);
        }

        /// <summary>
        /// Check if string contains only letters and digits
        /// </summary>
        public static bool IsAlphanumeric(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return false;

            return str.All(char.IsLetterOrDigit);
        }

        #endregion

        #region Extraction

        /// <summary>
        /// Extract numbers from string
        /// </summary>
        public static string ExtractNumbers(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            return new string(str.Where(char.IsDigit).ToArray());
        }

        /// <summary>
        /// Extract letters from string
        /// </summary>
        public static string ExtractLetters(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            return new string(str.Where(char.IsLetter).ToArray());
        }

        /// <summary>
        /// Get first N characters
        /// </summary>
        public static string Left(this string str, int length)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            return str.Length <= length ? str : str.Substring(0, length);
        }

        /// <summary>
        /// Get last N characters
        /// </summary>
        public static string Right(this string str, int length)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            return str.Length <= length ? str : str.Substring(str.Length - length);
        }

        #endregion

        #region Comparison

        /// <summary>
        /// Case-insensitive equals
        /// </summary>
        public static bool EqualsIgnoreCase(this string str, string other)
        {
            return string.Equals(str, other, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Case-insensitive contains
        /// </summary>
        public static bool ContainsIgnoreCase(this string str, string value)
        {
            if (str == null || value == null)
                return false;

            return str.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Case-insensitive starts with
        /// </summary>
        public static bool StartsWithIgnoreCase(this string str, string value)
        {
            if (str == null || value == null)
                return false;

            return str.StartsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Case-insensitive ends with
        /// </summary>
        public static bool EndsWithIgnoreCase(this string str, string value)
        {
            if (str == null || value == null)
                return false;

            return str.EndsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Hashing

        /// <summary>
        /// Generate MD5 hash
        /// </summary>
        /// <remarks>
        /// WARNING: MD5 is cryptographically broken and should NOT be used for security purposes.
        /// Use ToSHA256() for secure hashing. This method is provided for compatibility only.
        /// </remarks>
        [Obsolete("MD5 is not secure. Use ToSHA256() for security-sensitive hashing.")]
        public static string ToMD5(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(str);
                var hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        /// <summary>
        /// Generate SHA256 hash
        /// </summary>
        public static string ToSHA256(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(str);
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        #endregion

        #region Encoding

        /// <summary>
        /// Convert to Base64
        /// </summary>
        public static string ToBase64(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            var bytes = Encoding.UTF8.GetBytes(str);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Decode from Base64
        /// </summary>
        public static string FromBase64(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            try
            {
                var bytes = Convert.FromBase64String(str);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return str;
            }
        }

        #endregion

        #region Pluralization

        /// <summary>
        /// Simple pluralization (adds 's')
        /// </summary>
        public static string Pluralize(this string str, int count)
        {
            if (count == 1)
                return str;

            // Simple rules
            if (str.EndsWith("y"))
                return str.Substring(0, str.Length - 1) + "ies";

            if (str.EndsWith("s") || str.EndsWith("x") || str.EndsWith("ch") || str.EndsWith("sh"))
                return str + "es";

            return str + "s";
        }

        #endregion

        #region Reverse

        /// <summary>
        /// Reverse string
        /// </summary>
        public static string Reverse(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            var charArray = str.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }

        #endregion

        #region Repeat

        /// <summary>
        /// Repeat string N times
        /// </summary>
        public static string Repeat(this string str, int count)
        {
            if (string.IsNullOrWhiteSpace(str) || count <= 0)
                return str;

            var sb = new StringBuilder(str.Length * count);
            for (int i = 0; i < count; i++)
            {
                sb.Append(str);
            }
            return sb.ToString();
        }

        #endregion

        #region Word Count

        /// <summary>
        /// Count words in string
        /// </summary>
        public static int WordCount(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return 0;

            return str.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        #endregion
    }
}
