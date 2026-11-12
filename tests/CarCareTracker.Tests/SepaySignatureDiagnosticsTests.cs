using CarCareTracker.Logic;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace CarCareTracker.Tests
{
    public class SepaySignatureDiagnosticsTests
    {
        private const string Secret = "diag-secret";
        private const string Body = "{\"id\":7,\"gateway\":\"MBBank\",\"transactionDate\":\"2026-11-12 16:44:00\",\"accountNumber\":\"0123456789\",\"content\":\"Chuyển tiền ăn trưa / Thỏ\",\"transferType\":\"out\",\"transferAmount\":50000,\"accumulated\":950000,\"referenceCode\":\"FT1\"}";
        private const long Now = 1_800_000_000;

        private static string Hex(string signed) =>
            "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();

        [Fact]
        public void ReportsRawMatchForTheDocumentedScheme()
        {
            var body = Encoding.UTF8.GetBytes(Body);
            var text = SepaySignatureDiagnostics.Describe(Secret, Now.ToString(), SepayRules.Sign(Secret, Now, body), body, Now + 3, "application/json");
            Assert.Contains("matchingVariants=ts.+raw", text);
            Assert.Contains("skewSeconds=3", text);
            Assert.Contains("bodyNonAscii=True", text);
            Assert.Contains("signatureEncoding=hex", text);
        }

        [Fact]
        public void FindsPhpStyleEncodingWhenTheSenderSignsEscapedJson()
        {
            var body = Encoding.UTF8.GetBytes(Body);
            var php = Body.Replace("/", "\\/");
            php = string.Concat(php.Select(c => c > 0x7F ? "\\u" + ((int)c).ToString("x4") : c.ToString()));
            var signature = Hex(Now + "." + php);
            Assert.False(SepayRules.VerifySignature(Secret, Now.ToString(), signature, body, Now));
            var text = SepaySignatureDiagnostics.Describe(Secret, Now.ToString(), signature, body, Now, "application/json");
            Assert.Contains("matchingVariants=ts.+phpDefault", text);
        }

        [Fact]
        public void ReportsBodyOnlySignatureAndMillisecondTimestamps()
        {
            var body = Encoding.UTF8.GetBytes(Body);
            var millis = (Now * 1000).ToString();
            var text = SepaySignatureDiagnostics.Describe(Secret, millis, Hex(Body), body, Now, null);
            Assert.Contains("matchingVariants=none+raw", text);
            Assert.Contains("timestampDigits=13", text);
            Assert.Contains("skewSecondsIfMillis=0", text);
        }

        [Fact]
        public void NeverWritesTheSecretOrSignature()
        {
            var body = Encoding.UTF8.GetBytes(Body);
            var signature = SepayRules.Sign(Secret, Now, body);
            var text = SepaySignatureDiagnostics.Describe(Secret, Now.ToString(), signature, body, Now, "application/json");
            Assert.DoesNotContain(Secret, text);
            Assert.DoesNotContain(signature.Substring(7), text);
            Assert.DoesNotContain("0123456789", text);
            Assert.DoesNotContain("50000", text);
        }

        [Fact]
        public void HandlesMissingHeadersAndNonJson()
        {
            var text = SepaySignatureDiagnostics.Describe(Secret, null, null, Encoding.UTF8.GetBytes("a=1&b=2"), Now, "application/x-www-form-urlencoded");
            Assert.Contains("timestamp=missing", text);
            Assert.Contains("signature=missing", text);
            Assert.Contains("contentType=application/x-www-form-urlencoded", text);
        }
    }
}
