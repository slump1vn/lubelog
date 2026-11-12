namespace CarCareTracker.Models
{
    public class AuthCookie
    {
        public UserData UserData { get; set; }
        public DateTime ExpiresOn { get; set; }
        /// <summary>The server-side session this cookie belongs to; a cookie without one is not accepted.</summary>
        public int SessionId { get; set; }
        /// <summary>Random secret whose hash is stored with the session.</summary>
        public string SessionSecret { get; set; } = string.Empty;
    }
}
