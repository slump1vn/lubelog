using CarCareTracker.Helper;
using Xunit;

namespace CarCareTracker.Tests
{
    public class PasswordHasherTests
    {
        [Fact]
        public void NewHashIsSaltedAndVerifies()
        {
            var first = PasswordHasher.Hash("correct horse battery");
            var second = PasswordHasher.Hash("correct horse battery");
            Assert.StartsWith("pbkdf2-sha256$600000$", first);
            Assert.NotEqual(first, second);
            Assert.True(PasswordHasher.Verify("correct horse battery", first, out var rehash));
            Assert.False(rehash);
            Assert.False(PasswordHasher.IsLegacyHash(first));
        }

        [Fact]
        public void WrongPasswordFails()
        {
            var stored = PasswordHasher.Hash("correct horse battery");
            Assert.False(PasswordHasher.Verify("Correct horse battery", stored, out var rehash));
            Assert.False(rehash);
        }

        [Fact]
        public void LegacySha256HashVerifiesAndAsksForRehash()
        {
            var legacy = StaticHelper.GetHash("old-password");
            Assert.True(PasswordHasher.IsLegacyHash(legacy));
            Assert.True(PasswordHasher.Verify("old-password", legacy, out var rehash));
            Assert.True(rehash);
            Assert.False(PasswordHasher.Verify("other", legacy, out rehash));
            Assert.False(rehash);
        }

        [Fact]
        public void WeakerWorkFactorAsksForRehash()
        {
            var salt = new byte[16];
            var key = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("pw-12345678", salt, 1000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            var stored = $"pbkdf2-sha256$1000${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
            Assert.True(PasswordHasher.Verify("pw-12345678", stored, out var rehash));
            Assert.True(rehash);
        }

        [Theory]
        [InlineData("")]
        [InlineData("pbkdf2-sha256$abc$x$y")]
        [InlineData("pbkdf2-sha256$1000$not-base64$%%%")]
        [InlineData("pbkdf2-sha256$1000$AAAA")]
        public void MalformedOrEmptyHashesNeverVerify(string stored)
        {
            Assert.False(PasswordHasher.Verify("anything", stored, out _));
        }
    }
}
