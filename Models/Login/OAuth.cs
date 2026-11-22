using System.Text.Json.Serialization;

namespace CarCareTracker.Models
{
    /// <summary>
    /// An app allowed to sign in through OAuth. Native apps are public clients (RFC 8252): they hold
    /// no secret, so every sign-in is bound to the device by PKCE and to an exact redirect address.
    /// </summary>
    public class OAuthClient
    {
        public string ClientId { get; set; } = string.Empty;
        /// <summary>Shown on the consent screen.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Compared character for character with the redirect_uri of each request.</summary>
        public List<string> RedirectUris { get; set; } = new();
    }
    /// <summary>A one-time code from the consent screen, kept in memory for a minute.</summary>
    public class OAuthAuthorizationCode
    {
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string CodeChallenge { get; set; } = string.Empty;
        public UserData User { get; set; } = new();
    }
    /// <summary>What a Bearer access token carries; it is encrypted and signed by ASP.NET Data Protection.</summary>
    public class OAuthAccessToken
    {
        public int SessionId { get; set; }
        public int UserId { get; set; }
    }
    /// <summary>The token endpoint's answer (RFC 6749 section 5.1), or an error (section 5.2).</summary>
    public class OAuthTokenResponse
    {
        [JsonPropertyName("access_token"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AccessToken { get; set; }
        [JsonPropertyName("token_type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TokenType { get; set; }
        [JsonPropertyName("expires_in"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ExpiresIn { get; set; }
        [JsonPropertyName("refresh_token"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RefreshToken { get; set; }
        [JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; set; }
        [JsonPropertyName("error_description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ErrorDescription { get; set; }
        [JsonIgnore]
        public bool Succeeded => Error == null;
        public static OAuthTokenResponse Fail(string error, string description) => new() { Error = error, ErrorDescription = description };
    }
    /// <summary>The consent screen.</summary>
    public class OAuthConsentViewModel
    {
        public string ClientName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string CodeChallenge { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}
