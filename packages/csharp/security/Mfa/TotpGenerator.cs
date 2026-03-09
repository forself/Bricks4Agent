using System;
using System.Security.Cryptography;
using System.Text;

namespace Bricks4Agent.Security.Mfa
{
    /// <summary>
    /// TOTP (Time-based One-Time Password) generator following RFC 6238
    /// Compatible with Google Authenticator, Microsoft Authenticator, Authy, etc.
    /// </summary>
    public class TotpGenerator
    {
        private const int DefaultTimeStep = 30; // seconds
        private const int DefaultCodeLength = 6;
        private const int DefaultSecretLength = 20; // bytes (160 bits)

        /// <summary>
        /// Generate a new random secret key for TOTP
        /// </summary>
        /// <param name="length">Length in bytes (default: 20 bytes = 160 bits)</param>
        /// <returns>Base32 encoded secret</returns>
        public static string GenerateSecret(int length = DefaultSecretLength)
        {
            if (length < 16)
                throw new ArgumentException("Secret length must be at least 16 bytes", nameof(length));

            var secretBytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(secretBytes);
            }
            return Base32Encode(secretBytes);
        }

        /// <summary>
        /// Generate current TOTP code
        /// </summary>
        /// <param name="secret">Base32 encoded secret</param>
        /// <param name="timeStep">Time step in seconds (default: 30)</param>
        /// <param name="codeLength">Code length (default: 6)</param>
        /// <returns>TOTP code as string</returns>
        public static string GenerateCode(string secret, int timeStep = DefaultTimeStep, int codeLength = DefaultCodeLength)
        {
            if (string.IsNullOrEmpty(secret))
                throw new ArgumentNullException(nameof(secret));

            var counter = GetCurrentCounter(timeStep);
            return GenerateCodeFromCounter(secret, counter, codeLength);
        }

        /// <summary>
        /// Validate a TOTP code with time window tolerance
        /// </summary>
        /// <param name="secret">Base32 encoded secret</param>
        /// <param name="code">User-provided code</param>
        /// <param name="timeStep">Time step in seconds (default: 30)</param>
        /// <param name="windowSize">Number of time steps to check before/after current (default: 1)</param>
        /// <returns>True if code is valid within the time window</returns>
        public static bool ValidateCode(string secret, string code, int timeStep = DefaultTimeStep, int windowSize = 1)
        {
            if (string.IsNullOrEmpty(secret))
                throw new ArgumentNullException(nameof(secret));

            if (string.IsNullOrEmpty(code))
                return false;

            // Sanitize code input
            code = code.Trim().Replace(" ", "").Replace("-", "");
            if (!IsNumericCode(code))
                return false;

            var currentCounter = GetCurrentCounter(timeStep);

            // Check codes within the time window
            for (int i = -windowSize; i <= windowSize; i++)
            {
                var expectedCode = GenerateCodeFromCounter(secret, currentCounter + i, code.Length);
                if (ConstantTimeEquals(code, expectedCode))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Generate otpauth:// URI for QR code
        /// </summary>
        /// <param name="secret">Base32 encoded secret</param>
        /// <param name="accountName">User identifier (usually email)</param>
        /// <param name="issuer">Application name</param>
        /// <param name="algorithm">Hash algorithm (default: SHA1)</param>
        /// <param name="digits">Code length (default: 6)</param>
        /// <param name="period">Time step in seconds (default: 30)</param>
        /// <returns>otpauth:// URI string</returns>
        public static string GenerateOtpAuthUri(
            string secret,
            string accountName,
            string issuer,
            string algorithm = "SHA1",
            int digits = DefaultCodeLength,
            int period = DefaultTimeStep)
        {
            if (string.IsNullOrEmpty(secret))
                throw new ArgumentNullException(nameof(secret));
            if (string.IsNullOrEmpty(accountName))
                throw new ArgumentNullException(nameof(accountName));
            if (string.IsNullOrEmpty(issuer))
                throw new ArgumentNullException(nameof(issuer));

            var encodedIssuer = Uri.EscapeDataString(issuer);
            var encodedAccount = Uri.EscapeDataString(accountName);

            return $"otpauth://totp/{encodedIssuer}:{encodedAccount}" +
                   $"?secret={secret}" +
                   $"&issuer={encodedIssuer}" +
                   $"&algorithm={algorithm}" +
                   $"&digits={digits}" +
                   $"&period={period}";
        }

        #region Private Methods

        private static long GetCurrentCounter(int timeStep)
        {
            var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return unixTime / timeStep;
        }

        private static string GenerateCodeFromCounter(string secret, long counter, int codeLength)
        {
            var secretBytes = Base32Decode(secret);
            var counterBytes = BitConverter.GetBytes(counter);

            // Ensure big-endian byte order
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(counterBytes);
            }

            // Compute HMAC-SHA1
            using var hmac = new HMACSHA1(secretBytes);
            var hash = hmac.ComputeHash(counterBytes);

            // Dynamic truncation
            var offset = hash[hash.Length - 1] & 0x0F;
            var binaryCode =
                ((hash[offset] & 0x7F) << 24) |
                ((hash[offset + 1] & 0xFF) << 16) |
                ((hash[offset + 2] & 0xFF) << 8) |
                (hash[offset + 3] & 0xFF);

            var otp = binaryCode % (int)Math.Pow(10, codeLength);
            return otp.ToString().PadLeft(codeLength, '0');
        }

        private static bool IsNumericCode(string code)
        {
            foreach (var c in code)
            {
                if (!char.IsDigit(c))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Constant-time string comparison to prevent timing attacks
        /// </summary>
        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            var result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }

        #endregion

        #region Base32 Encoding

        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        /// <summary>
        /// Encode bytes to Base32 string
        /// </summary>
        public static string Base32Encode(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            var result = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = data[0];
            int bitsLeft = 8;
            int index = 0;

            while (bitsLeft > 0 || index < data.Length)
            {
                if (bitsLeft < 5)
                {
                    if (index < data.Length - 1)
                    {
                        buffer <<= 8;
                        buffer |= data[++index];
                        bitsLeft += 8;
                    }
                    else
                    {
                        int pad = 5 - bitsLeft;
                        buffer <<= pad;
                        bitsLeft += pad;
                    }
                }

                int charIndex = (buffer >> (bitsLeft - 5)) & 0x1F;
                bitsLeft -= 5;
                result.Append(Base32Alphabet[charIndex]);
            }

            return result.ToString();
        }

        /// <summary>
        /// Decode Base32 string to bytes
        /// </summary>
        public static byte[] Base32Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded))
                return Array.Empty<byte>();

            // Normalize input
            encoded = encoded.ToUpperInvariant().Replace(" ", "").Replace("-", "");

            // Remove padding
            encoded = encoded.TrimEnd('=');

            var result = new byte[encoded.Length * 5 / 8];
            int buffer = 0;
            int bitsLeft = 0;
            int index = 0;

            foreach (var c in encoded)
            {
                int charValue = Base32Alphabet.IndexOf(c);
                if (charValue < 0)
                    throw new FormatException($"Invalid Base32 character: {c}");

                buffer <<= 5;
                buffer |= charValue;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    result[index++] = (byte)(buffer >> (bitsLeft - 8));
                    bitsLeft -= 8;
                }
            }

            return result;
        }

        #endregion
    }
}
