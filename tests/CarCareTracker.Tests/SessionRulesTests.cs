using CarCareTracker.Logic;
using CarCareTracker.Models;
using Xunit;

namespace CarCareTracker.Tests
{
    public class SessionRulesTests
    {
        [Fact]
        public void SecretsAreLongRandomAndOnlyTheirHashMatches()
        {
            var first = SessionRules.NewSecret();
            var second = SessionRules.NewSecret();
            Assert.NotEqual(first, second);
            Assert.Equal(43, first.Length); //32 bytes in base64url without padding
            Assert.DoesNotContain('+', first + second);
            Assert.DoesNotContain('/', first + second);
            var hash = SessionRules.Hash(first);
            Assert.NotEqual(first, hash);
            Assert.True(SessionRules.Matches(first, hash));
            Assert.False(SessionRules.Matches(second, hash));
            Assert.False(SessionRules.Matches(null, hash));
            Assert.False(SessionRules.Matches(first, string.Empty));
        }

        [Fact]
        public void EndedOrExpiredSessionsAreNotActive()
        {
            var now = new DateTime(2026, 11, 12, 5, 0, 0, DateTimeKind.Utc);
            Assert.True(SessionRules.IsActive(new UserSession { ExpiresUtc = now.AddDays(1) }, now));
            Assert.False(SessionRules.IsActive(new UserSession { ExpiresUtc = now }, now));
            Assert.False(SessionRules.IsActive(new UserSession { ExpiresUtc = now.AddDays(1), RevokedUtc = now.AddMinutes(-1) }, now));
        }

        [Fact]
        public void LastSeenIsWrittenAtMostEveryFiveMinutes()
        {
            var now = new DateTime(2026, 11, 12, 5, 0, 0, DateTimeKind.Utc);
            Assert.False(SessionRules.ShouldRecordSeen(now.AddMinutes(-4), now));
            Assert.True(SessionRules.ShouldRecordSeen(now.AddMinutes(-5), now));
        }

        [Theory]
        [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "iPhone · Safari")]
        [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/130.0 Mobile/15E148 Safari/604.1", "iPhone · Chrome")]
        [InlineData("Mozilla/5.0 (Linux; Android 14; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36", "Android · Chrome")]
        [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0", "Windows · Edge")]
        [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7; rv:132.0) Gecko/20100101 Firefox/132.0", "Mac · Firefox")]
        [InlineData("curl/8.5.0", "")]
        [InlineData(null, "")]
        public void DeviceNamesComeFromTheUserAgent(string? userAgent, string expected)
        {
            Assert.Equal(expected, SessionRules.DescribeDevice(userAgent));
        }
    }
}
