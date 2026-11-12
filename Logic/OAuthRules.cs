using CarCareTracker.Models;
using System.Security.Cryptography;
using System.Text;

namespace CarCareTracker.Logic
{
    /// <summary>
    /// OAuth 2.0 rules for native apps (RFC 6749, PKCE RFC 7636, RFC 8252, Security BCP RFC 9700),
    /// as pure functions so they can be tested.
    /// </summary>
    public static class OAuthRules
    {
        public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(1);
        /// <summary>The iPhone app. Its redirect is a private-use scheme in reverse-domain form (RFC 8252 section 7.1).</summary>
        public const string IosClientId = "soitho-ios";
        public const string IosRedirectUri = "vn.digifusion.finance:/oauth/callback";
        public static readonly IReadOnlyList<OAuthClient> Clients = new List<OAuthClient>
        {
            new() { ClientId = IosClientId, Name = "Sói & Thỏ cho iPhone", RedirectUris = { IosRedirectUri } }
        };

        public static OAuthClient? FindClient(string? clientId)
        {
            return Clients.FirstOrDefault(x => x.ClientId == clientId);
        }
        /// <summary>Exact string match, as RFC 9700 section 4.1.3 asks: no prefixes, no wildcards.</summary>
        public static bool IsRegisteredRedirect(OAuthClient client, string? redirectUri)
        {
            return !string.IsNullOrEmpty(redirectUri) && client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal);
        }
        /// <summary>A code_verifier or S256 challenge: 43 to 128 characters of A-Z a-z 0-9 - . _ ~ (RFC 7636 section 4.1).</summary>
        public static bool IsWellFormedPkceValue(string? value)
        {
            return !string.IsNullOrEmpty(value) && value.Length >= 43 && value.Length <= 128
                && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '.' || c == '_' || c == '~');
        }
        /// <summary>BASE64URL(SHA256(ASCII(code_verifier))) == code_challenge, in constant time.</summary>
        public static bool VerifyPkce(string? codeVerifier, string? codeChallenge)
        {
            if (!IsWellFormedPkceValue(codeVerifier) || !IsWellFormedPkceValue(codeChallenge))
            {
                return false;
            }
            var computed = Encoding.ASCII.GetBytes(S256(codeVerifier!));
            return CryptographicOperations.FixedTimeEquals(computed, Encoding.ASCII.GetBytes(codeChallenge!));
        }
        public static string S256(string codeVerifier)
        {
            return Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        /// <summary>A refresh token is "sessionId.secret", so the session can be found without scanning.</summary>
        public static string FormatRefreshToken(int sessionId, string secret)
        {
            return $"{sessionId}.{secret}";
        }
        public static bool TryParseRefreshToken(string? token, out int sessionId, out string secret)
        {
            sessionId = 0;
            secret = string.Empty;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }
            var dot = token.IndexOf('.');
            if (dot <= 0 || dot == token.Length - 1 || !int.TryParse(token.AsSpan(0, dot), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out sessionId))
            {
                sessionId = 0;
                return false;
            }
            secret = token[(dot + 1)..];
            return true;
        }
        /// <summary>Appends the parameters to the redirect address, after a "?" or "&amp;" as needed.</summary>
        public static string BuildRedirect(string redirectUri, IEnumerable<KeyValuePair<string, string>> parameters)
        {
            var query = string.Join("&", parameters.Where(x => !string.IsNullOrEmpty(x.Value)).Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
            if (query.Length == 0)
            {
                return redirectUri;
            }
            return redirectUri + (redirectUri.Contains('?') ? "&" : "?") + query;
        }
    }
}
