using CarCareTracker.Helper;
using Microsoft.AspNetCore.Http;
using System.Net;
using Xunit;

namespace CarCareTracker.Tests
{
    public class LoginSecurityTests
    {
        private static HttpRequest Request(string scheme)
        {
            var context = new DefaultHttpContext();
            context.Request.Scheme = scheme;
            return context.Request;
        }

        [Fact]
        public void LoginCookieIsHiddenFromScriptsAndOtherSites()
        {
            var options = LoginSecurity.CookieOptions(Request("https"), new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.True(options.HttpOnly);
            Assert.True(options.Secure);
            Assert.Equal(SameSiteMode.Lax, options.SameSite);
            Assert.Equal("/", options.Path);
            Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), options.Expires);
        }

        [Fact]
        public void PlainHttpStillWorksOnTheLocalNetwork()
        {
            var options = LoginSecurity.CookieOptions(Request("http"), null);
            Assert.False(options.Secure);
            Assert.True(options.HttpOnly);
            Assert.Null(options.Expires);
        }

        [Fact]
        public void AttemptsAreCountedPerAddress()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
            Assert.Equal("203.0.113.7", LoginSecurity.ClientKey(context));
            Assert.Equal("unknown", LoginSecurity.ClientKey(new DefaultHttpContext()));
        }
    }
}
