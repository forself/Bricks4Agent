using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Bricks4Agent.Security.RateLimiting
{
    /// <summary>
    /// Client connection information
    /// </summary>
    public class ClientConnectionInfo
    {
        /// <summary>
        /// Client IP address
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>
        /// Whether IP is from a proxy (X-Forwarded-For)
        /// </summary>
        public bool IsProxied { get; set; }

        /// <summary>
        /// Original IP if proxied
        /// </summary>
        public string OriginalIp { get; set; } = string.Empty;

        /// <summary>
        /// All IPs in the chain (for proxied requests)
        /// </summary>
        public List<string> IpChain { get; set; } = new();

        /// <summary>
        /// User agent string
        /// </summary>
        public string UserAgent { get; set; } = string.Empty;

        /// <summary>
        /// Parsed user agent info
        /// </summary>
        public UserAgentInfo UserAgentInfo { get; set; } = new();

        /// <summary>
        /// Request origin/referer
        /// </summary>
        public string Referer { get; set; } = string.Empty;

        /// <summary>
        /// Accept-Language header
        /// </summary>
        public string AcceptLanguage { get; set; } = string.Empty;

        /// <summary>
        /// Request host
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// Request scheme (http/https)
        /// </summary>
        public string Scheme { get; set; } = string.Empty;

        /// <summary>
        /// Request path
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Request method
        /// </summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>
        /// Connection timestamp
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Unique fingerprint based on connection characteristics
        /// </summary>
        public string Fingerprint { get; set; } = string.Empty;

        /// <summary>
        /// Whether this appears to be a bot/crawler
        /// </summary>
        public bool IsSuspectedBot { get; set; }

        /// <summary>
        /// Country code (if geo-IP is configured)
        /// </summary>
        public string CountryCode { get; set; } = string.Empty;

        /// <summary>
        /// Whether connection is secure (HTTPS)
        /// </summary>
        public bool IsSecure => Scheme?.Equals("https", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    /// <summary>
    /// Parsed user agent information
    /// </summary>
    public class UserAgentInfo
    {
        /// <summary>
        /// Browser name
        /// </summary>
        public string Browser { get; set; } = string.Empty;

        /// <summary>
        /// Browser version
        /// </summary>
        public string BrowserVersion { get; set; } = string.Empty;

        /// <summary>
        /// Operating system
        /// </summary>
        public string OS { get; set; } = string.Empty;

        /// <summary>
        /// Device type (Desktop, Mobile, Tablet, Bot)
        /// </summary>
        public string DeviceType { get; set; } = string.Empty;

        /// <summary>
        /// Whether this is a mobile device
        /// </summary>
        public bool IsMobile { get; set; }

        /// <summary>
        /// Whether this is a bot/crawler
        /// </summary>
        public bool IsBot { get; set; }
    }

    /// <summary>
    /// Service interface for extracting connection information
    /// </summary>
    public interface IConnectionInfoService
    {
        /// <summary>
        /// Get connection info from HTTP context
        /// </summary>
        ClientConnectionInfo GetConnectionInfo(HttpContext context);

        /// <summary>
        /// Get client IP address
        /// </summary>
        string GetClientIp(HttpContext context);

        /// <summary>
        /// Get user agent info
        /// </summary>
        UserAgentInfo ParseUserAgent(string userAgent);

        /// <summary>
        /// Generate a fingerprint for the connection
        /// </summary>
        string GenerateFingerprint(HttpContext context);
    }

    /// <summary>
    /// Connection info service implementation
    /// </summary>
    public class ConnectionInfoService : IConnectionInfoService
    {
        private readonly ConnectionInfoOptions _options;

        // Known bot patterns
        private static readonly string[] BotPatterns = new[]
        {
            "bot", "crawler", "spider", "slurp", "googlebot", "bingbot",
            "yandex", "baidu", "duckduckbot", "facebookexternalhit",
            "twitterbot", "linkedinbot", "embedly", "quora", "pinterest",
            "slack", "discord", "telegram", "whatsapp", "viber"
        };

        // Known mobile patterns
        private static readonly string[] MobilePatterns = new[]
        {
            "mobile", "android", "iphone", "ipad", "ipod", "blackberry",
            "windows phone", "opera mini", "opera mobi"
        };

        public ConnectionInfoService(ConnectionInfoOptions? options = null)
        {
            _options = options ?? new ConnectionInfoOptions();
        }

        /// <inheritdoc />
        public ClientConnectionInfo GetConnectionInfo(HttpContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var request = context.Request;
            var userAgent = request.Headers["User-Agent"].ToString();

            var info = new ClientConnectionInfo
            {
                IpAddress = GetClientIp(context),
                UserAgent = userAgent,
                UserAgentInfo = ParseUserAgent(userAgent),
                Referer = request.Headers["Referer"].ToString(),
                AcceptLanguage = request.Headers["Accept-Language"].ToString(),
                Host = request.Host.ToString(),
                Scheme = request.Scheme,
                Path = request.Path.ToString(),
                Method = request.Method,
                Timestamp = DateTime.UtcNow
            };

            // Check for proxy
            var forwardedFor = request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                info.IsProxied = true;
                info.IpChain = forwardedFor.Split(',')
                    .Select(ip => ip.Trim())
                    .Where(ip => !string.IsNullOrEmpty(ip))
                    .ToList();
                info.OriginalIp = info.IpChain.FirstOrDefault() ?? string.Empty;
            }

            // Generate fingerprint
            info.Fingerprint = GenerateFingerprint(context);

            // Check if bot
            info.IsSuspectedBot = info.UserAgentInfo?.IsBot ?? false;

            return info;
        }

        /// <inheritdoc />
        public string GetClientIp(HttpContext context)
        {
            if (context == null)
                return string.Empty;

            string? ip = null;

            // Check trusted proxy headers (in order of preference)
            if (_options.TrustProxyHeaders)
            {
                // CF-Connecting-IP (Cloudflare)
                ip = context.Request.Headers["CF-Connecting-IP"].ToString();
                if (!string.IsNullOrEmpty(ip) && IsValidIp(ip))
                    return NormalizeIp(ip);

                // X-Real-IP (nginx)
                ip = context.Request.Headers["X-Real-IP"].ToString();
                if (!string.IsNullOrEmpty(ip) && IsValidIp(ip))
                    return NormalizeIp(ip);

                // X-Forwarded-For (standard proxy header)
                var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
                if (!string.IsNullOrEmpty(forwardedFor))
                {
                    // Take the first IP (original client)
                    ip = forwardedFor.Split(',').FirstOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(ip) && IsValidIp(ip))
                        return NormalizeIp(ip);
                }
            }

            // Fall back to remote IP
            ip = context.Connection.RemoteIpAddress?.ToString();
            return NormalizeIp(ip ?? string.Empty);
        }

        /// <inheritdoc />
        public UserAgentInfo ParseUserAgent(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
            {
                return new UserAgentInfo
                {
                    Browser = "Unknown",
                    OS = "Unknown",
                    DeviceType = "Unknown"
                };
            }

            var ua = userAgent.ToLowerInvariant();
            var info = new UserAgentInfo();

            // Detect bot
            info.IsBot = BotPatterns.Any(pattern => ua.Contains(pattern));
            if (info.IsBot)
            {
                info.DeviceType = "Bot";
                info.Browser = DetectBotName(ua);
                return info;
            }

            // Detect mobile
            info.IsMobile = MobilePatterns.Any(pattern => ua.Contains(pattern));
            info.DeviceType = info.IsMobile ? "Mobile" : "Desktop";

            // Detect OS
            info.OS = DetectOS(ua);

            // Detect browser
            (info.Browser, info.BrowserVersion) = DetectBrowser(ua);

            return info;
        }

        /// <inheritdoc />
        public string GenerateFingerprint(HttpContext context)
        {
            if (context == null)
                return string.Empty;

            var request = context.Request;

            // Combine various headers for fingerprinting
            var components = new List<string>
            {
                GetClientIp(context),
                request.Headers["User-Agent"].ToString(),
                request.Headers["Accept-Language"].ToString(),
                request.Headers["Accept-Encoding"].ToString(),
                request.Headers["Accept"].ToString()
            };

            var combined = string.Join("|", components.Where(c => !string.IsNullOrEmpty(c)));

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            return Convert.ToBase64String(hash).Substring(0, 16);
        }

        #region Private Methods

        private static bool IsValidIp(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return false;

            return IPAddress.TryParse(ip.Trim(), out _);
        }

        private static string NormalizeIp(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return string.Empty;

            ip = ip.Trim();

            // Handle IPv6 localhost
            if (ip == "::1")
                return "127.0.0.1";

            // Handle IPv4-mapped IPv6 addresses
            if (ip.StartsWith("::ffff:"))
                return ip.Substring(7);

            return ip;
        }

        private static string DetectBotName(string ua)
        {
            if (ua.Contains("googlebot")) return "Googlebot";
            if (ua.Contains("bingbot")) return "Bingbot";
            if (ua.Contains("yandex")) return "Yandex";
            if (ua.Contains("baidu")) return "Baidu";
            if (ua.Contains("duckduckbot")) return "DuckDuckBot";
            if (ua.Contains("facebookexternalhit")) return "Facebook";
            if (ua.Contains("twitterbot")) return "Twitter";
            return "Bot";
        }

        private static string DetectOS(string ua)
        {
            if (ua.Contains("windows nt 10")) return "Windows 10/11";
            if (ua.Contains("windows nt 6.3")) return "Windows 8.1";
            if (ua.Contains("windows nt 6.2")) return "Windows 8";
            if (ua.Contains("windows nt 6.1")) return "Windows 7";
            if (ua.Contains("windows")) return "Windows";
            if (ua.Contains("mac os x")) return "macOS";
            if (ua.Contains("android")) return "Android";
            if (ua.Contains("iphone") || ua.Contains("ipad")) return "iOS";
            if (ua.Contains("linux")) return "Linux";
            return "Unknown";
        }

        private static (string Browser, string Version) DetectBrowser(string ua)
        {
            // Order matters - check more specific patterns first
            if (ua.Contains("edg/"))
                return ("Edge", ExtractVersion(ua, "edg/"));
            if (ua.Contains("edge/"))
                return ("Edge", ExtractVersion(ua, "edge/"));
            if (ua.Contains("opr/") || ua.Contains("opera"))
                return ("Opera", ExtractVersion(ua, "opr/"));
            if (ua.Contains("chrome/") && !ua.Contains("chromium"))
                return ("Chrome", ExtractVersion(ua, "chrome/"));
            if (ua.Contains("firefox/"))
                return ("Firefox", ExtractVersion(ua, "firefox/"));
            if (ua.Contains("safari/") && !ua.Contains("chrome"))
                return ("Safari", ExtractVersion(ua, "version/"));
            if (ua.Contains("msie") || ua.Contains("trident"))
                return ("Internet Explorer", ExtractVersion(ua, "msie "));

            return ("Unknown", string.Empty);
        }

        private static string ExtractVersion(string ua, string marker)
        {
            var index = ua.IndexOf(marker);
            if (index < 0) return string.Empty;

            var start = index + marker.Length;
            var end = start;

            while (end < ua.Length && (char.IsDigit(ua[end]) || ua[end] == '.'))
            {
                end++;
            }

            return end > start ? ua.Substring(start, end - start) : string.Empty;
        }

        #endregion
    }

    /// <summary>
    /// Connection info service options
    /// </summary>
    public class ConnectionInfoOptions
    {
        /// <summary>
        /// Whether to trust proxy headers (X-Forwarded-For, etc.)
        /// Enable only if behind a trusted reverse proxy
        /// </summary>
        public bool TrustProxyHeaders { get; set; } = true;

        /// <summary>
        /// List of trusted proxy IPs (if empty, all proxies are trusted when TrustProxyHeaders is true)
        /// </summary>
        public List<string> TrustedProxies { get; set; } = new();
    }
}
