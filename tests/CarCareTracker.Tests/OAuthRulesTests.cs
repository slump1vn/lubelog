using CarCareTracker.Logic;
using Xunit;

namespace CarCareTracker.Tests
{
    public class OAuthRulesTests
    {
        //the example from RFC 7636 appendix B.
        private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        [Fact]
        public void PkceMatchesTheRfcExample()
        {
            Assert.Equal(Challenge, OAuthRules.S256(Verifier));
            Assert.True(OAuthRules.VerifyPkce(Verifier, Challenge));
            Assert.False(OAuthRules.VerifyPkce(Verifier.Replace('d', 'e'), Challenge));
            //the "plain" method, where the verifier is sent as the challenge, is not accepted.
            Assert.False(OAuthRules.VerifyPkce(Verifier, Verifier));
            Assert.False(OAuthRules.VerifyPkce(null, Challenge));
        }

        [Fact]
        public void PkceValuesMustBeLongAndUnreserved()
        {
            Assert.True(OAuthRules.IsWellFormedPkceValue(Verifier));
            Assert.False(OAuthRules.IsWellFormedPkceValue(new string('a', 42)));
            Assert.False(OAuthRules.IsWellFormedPkceValue(new string('a', 129)));
            Assert.False(OAuthRules.IsWellFormedPkceValue(new string('a', 42) + "="));
        }

        [Fact]
        public void RedirectsMustMatchExactly()
        {
            var client = OAuthRules.FindClient(OAuthRules.IosClientId)!;
            Assert.True(OAuthRules.IsRegisteredRedirect(client, OAuthRules.IosRedirectUri));
            Assert.False(OAuthRules.IsRegisteredRedirect(client, OAuthRules.IosRedirectUri + "/extra"));
            Assert.False(OAuthRules.IsRegisteredRedirect(client, "https://example.com/callback"));
            Assert.False(OAuthRules.IsRegisteredRedirect(client, null));
            Assert.Null(OAuthRules.FindClient("someone-else"));
        }

        [Fact]
        public void RefreshTokensCarryTheirSession()
        {
            var token = OAuthRules.FormatRefreshToken(42, "secret-part");
            Assert.True(OAuthRules.TryParseRefreshToken(token, out int id, out string secret));
            Assert.Equal(42, id);
            Assert.Equal("secret-part", secret);
            Assert.False(OAuthRules.TryParseRefreshToken("no-dot", out _, out _));
            Assert.False(OAuthRules.TryParseRefreshToken("-1.secret", out _, out _));
            Assert.False(OAuthRules.TryParseRefreshToken("42.", out _, out _));
            Assert.False(OAuthRules.TryParseRefreshToken(null, out _, out _));
        }

        [Fact]
        public void RedirectParametersAreEscaped()
        {
            Assert.Equal("app:/cb?code=a%2Bb&state=x%20y",
                OAuthRules.BuildRedirect("app:/cb", new Dictionary<string, string> { { "code", "a+b" }, { "state", "x y" }, { "empty", "" } }));
            Assert.Equal("app:/cb?x=1&error=access_denied",
                OAuthRules.BuildRedirect("app:/cb?x=1", new Dictionary<string, string> { { "error", "access_denied" } }));
        }

        [Fact]
        public void DeviceNamesAreShortAndPrintable()
        {
            Assert.Equal("iPhone của Sói", OAuthLogic.CleanDeviceName("  iPhone của Sói\n"));
            Assert.Equal(60, OAuthLogic.CleanDeviceName(new string('x', 100)).Length);
            Assert.Equal(string.Empty, OAuthLogic.CleanDeviceName(null));
        }
    }
}
