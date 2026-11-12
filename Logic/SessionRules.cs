using CarCareTracker.Models;
using System.Security.Cryptography;
using System.Text;

namespace CarCareTracker.Logic
{
    /// <summary>
    /// Rules for per-device sessions, as pure functions so they can be tested without storage.
    /// </summary>
    public static class SessionRules
    {
        /// <summary>How often the last-seen time is written, so that each request does not write.</summary>
        public static readonly TimeSpan SeenInterval = TimeSpan.FromMinutes(5);
        /// <summary>Ended sessions are kept this long for the device list, then deleted.</summary>
        public static readonly TimeSpan KeepEnded = TimeSpan.FromDays(30);

        /// <summary>A new secret with 256 bits from the system's secure random generator, base64url.</summary>
        public static string NewSecret()
        {
            return Base64Url(RandomNumberGenerator.GetBytes(32));
        }
        /// <summary>SHA-256 of the secret, base64url. The secret is random, so no salt or slow hash is needed.</summary>
        public static string Hash(string secret)
        {
            return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        }
        /// <summary>True when the secret matches the stored hash, compared in constant time.</summary>
        public static bool Matches(string? secret, string storedHash)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }
            var actual = Encoding.ASCII.GetBytes(Hash(secret));
            var expected = Encoding.ASCII.GetBytes(storedHash);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        public static bool IsActive(UserSession session, DateTime nowUtc)
        {
            return session.RevokedUtc == null && session.ExpiresUtc > nowUtc;
        }
        public static bool ShouldRecordSeen(DateTime lastSeenUtc, DateTime nowUtc)
        {
            return nowUtc - lastSeenUtc >= SeenInterval;
        }
        /// <summary>
        /// A short name for the device list from the browser's User-Agent, e.g. "iPhone · Safari".
        /// Empty when nothing is recognised. Only for display: the header is easy to fake.
        /// </summary>
        public static string DescribeDevice(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
            {
                return string.Empty;
            }
            string Has(params string[] parts) => parts.FirstOrDefault(x => userAgent.Contains(x, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            string device = Has("iPhone") != "" ? "iPhone"
                : Has("iPad") != "" ? "iPad"
                : Has("Android") != "" ? "Android"
                : Has("Windows") != "" ? "Windows"
                : Has("Macintosh", "Mac OS X") != "" ? "Mac"
                : Has("CrOS") != "" ? "ChromeOS"
                : Has("Linux") != "" ? "Linux"
                : string.Empty;
            string browser = Has("Edg/", "EdgA/", "EdgiOS/") != "" ? "Edge"
                : Has("OPR/") != "" ? "Opera"
                : Has("SamsungBrowser/") != "" ? "Samsung Internet"
                : Has("CriOS/", "Chrome/") != "" ? "Chrome"
                : Has("FxiOS/", "Firefox/") != "" ? "Firefox"
                : Has("Safari/") != "" ? "Safari"
                : string.Empty;
            return string.Join(" · ", new[] { device, browser }.Where(x => x.Length > 0));
        }
        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
