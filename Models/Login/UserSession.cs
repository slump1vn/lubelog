namespace CarCareTracker.Models
{
    public enum SessionKind
    {
        /// <summary>Signed in on the website (or the Android app, which shares the browser's cookie).</summary>
        Browser = 0,
        /// <summary>Signed in from a native app through OAuth; holds a refresh token.</summary>
        App = 1
    }
    /// <summary>
    /// One signed-in device. The server keeps one record per sign-in so that each device can be
    /// listed and signed out on its own (OWASP Session Management). Only hashes of the secrets are
    /// stored: a copy of the database is not enough to take over a session.
    /// </summary>
    public class UserSession
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public SessionKind Kind { get; set; }
        /// <summary>SHA-256 of the secret in the login cookie, base64url.</summary>
        public string SecretHash { get; set; } = string.Empty;
        /// <summary>SHA-256 of the current refresh token for an app session, base64url; empty otherwise.</summary>
        public string RefreshTokenHash { get; set; } = string.Empty;
        /// <summary>The OAuth client of an app session, e.g. the iOS app; empty for the browser.</summary>
        public string ClientId { get; set; } = string.Empty;
        /// <summary>Short description for the device list, e.g. "iPhone · Safari".</summary>
        public string DeviceName { get; set; } = string.Empty;
        public string LastAddress { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        /// <summary>Set when the user or the server ends the session; it then never works again.</summary>
        public DateTime? RevokedUtc { get; set; }
    }
    /// <summary>A row of the device list in the account settings.</summary>
    public class UserSessionViewModel
    {
        public int Id { get; set; }
        public SessionKind Kind { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string LastAddress { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public bool IsCurrent { get; set; }
    }
}
