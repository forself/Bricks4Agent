using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Bricks4Agent.Security.JWT
{
    /// <summary>
    /// JWT token generation and validation helper
    /// </summary>
    public class JwtHelper
    {
        private readonly IConfiguration _configuration;
        private readonly string _secretKey;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _expirationMinutes;

        /// <summary>
        /// Constructor
        /// </summary>
        public JwtHelper(IConfiguration configuration)
        {
            _configuration = configuration;
            _secretKey = configuration["Jwt:SecretKey"] ?? throw new ArgumentNullException("Jwt:SecretKey is not configured");
            _issuer = configuration["Jwt:Issuer"] ?? "YourApp";
            _audience = configuration["Jwt:Audience"] ?? "YourAppUsers";

            // Use TryParse to safely handle invalid configuration values
            var expirationConfig = configuration["Jwt:ExpirationMinutes"];
            if (!int.TryParse(expirationConfig, out _expirationMinutes) || _expirationMinutes <= 0)
            {
                _expirationMinutes = 60; // Default to 60 minutes
            }
        }

        /// <summary>
        /// Generate JWT access token
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="username">Username</param>
        /// <param name="email">User email</param>
        /// <param name="roles">User roles</param>
        /// <param name="additionalClaims">Additional custom claims</param>
        /// <returns>JWT token string</returns>
        public string GenerateToken(
            int userId,
            string username,
            string email,
            IEnumerable<string> roles = null,
            Dictionary<string, string> additionalClaims = null)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Email, email),
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            };

            // Add roles
            if (roles != null)
            {
                claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
            }

            // Add additional claims
            if (additionalClaims != null)
            {
                claims.AddRange(additionalClaims.Select(kvp => new Claim(kvp.Key, kvp.Value)));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(_expirationMinutes);

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: expires,
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Generate refresh token
        /// </summary>
        /// <returns>Refresh token string</returns>
        public string GenerateRefreshToken()
        {
            var randomBytes = new byte[64];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return Convert.ToBase64String(randomBytes);
        }

        /// <summary>
        /// Validate JWT token and return claims principal
        /// </summary>
        /// <param name="token">JWT token string</param>
        /// <returns>ClaimsPrincipal if valid, null otherwise</returns>
        public ClaimsPrincipal ValidateToken(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_secretKey);

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _issuer,
                    ValidAudience = _audience,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.Zero // Disable default 5-minute tolerance
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);

                // Verify token is actually a JWT token
                if (validatedToken is not JwtSecurityToken jwtToken ||
                    !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                {
                    return null;
                }

                return principal;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get user ID from token
        /// </summary>
        /// <param name="token">JWT token string</param>
        /// <returns>User ID if valid, null otherwise</returns>
        public int? GetUserIdFromToken(string token)
        {
            var principal = ValidateToken(token);
            if (principal == null)
                return null;

            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int userId))
                return userId;

            return null;
        }

        /// <summary>
        /// Get username from token
        /// </summary>
        /// <param name="token">JWT token string</param>
        /// <returns>Username if valid, null otherwise</returns>
        public string GetUsernameFromToken(string token)
        {
            var principal = ValidateToken(token);
            return principal?.FindFirst(ClaimTypes.Name)?.Value;
        }

        /// <summary>
        /// Get email from token
        /// </summary>
        /// <param name="token">JWT token string</param>
        /// <returns>Email if valid, null otherwise</returns>
        public string GetEmailFromToken(string token)
        {
            var principal = ValidateToken(token);
            return principal?.FindFirst(ClaimTypes.Email)?.Value;
        }

        /// <summary>
        /// Get roles from token
        /// </summary>
        /// <param name="token">JWT token string</param>
        /// <returns>List of roles if valid, empty list otherwise</returns>
        public List<string> GetRolesFromToken(string token)
        {
            var principal = ValidateToken(token);
            if (principal == null)
                return new List<string>();

            return principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        }

        /// <summary>
        /// Get custom claim from token
        /// </summary>
        /// <param name="token">JWT token string</param>
        /// <param name="claimType">Claim type</param>
        /// <returns>Claim value if exists, null otherwise</returns>
        public string GetClaimFromToken(string token, string claimType)
        {
            var principal = ValidateToken(token);
            return principal?.FindFirst(claimType)?.Value;
        }

        /// <summary>
        /// Check if token is expired
        /// </summary>
        /// <param name="token">JWT token string</param>
        /// <returns>True if expired, false otherwise</returns>
        public bool IsTokenExpired(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(token);
                return jwtToken.ValidTo < DateTime.UtcNow;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Get token expiration time
        /// </summary>
        /// <param name="token">JWT token string</param>
        /// <returns>Expiration DateTime if valid, null otherwise</returns>
        public DateTime? GetTokenExpiration(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(token);
                return jwtToken.ValidTo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get all claims from token
        /// </summary>
        /// <param name="token">JWT token string</param>
        /// <returns>Dictionary of claims if valid, empty dictionary otherwise</returns>
        /// <remarks>
        /// If there are duplicate claim types, only the first value is returned.
        /// Use GetRolesFromToken() for role claims which may have multiple values.
        /// </remarks>
        public Dictionary<string, string> GetAllClaims(string token)
        {
            var principal = ValidateToken(token);
            if (principal == null)
                return new Dictionary<string, string>();

            // Use GroupBy to handle duplicate claim types safely
            return principal.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.First().Value);
        }
    }
}
