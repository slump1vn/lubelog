using System.Security.Cryptography;
using System.Text;

namespace CarCareTracker.Helper
{
    /// <summary>
    /// Salted, slow password hashing (PBKDF2-HMAC-SHA256, OWASP 2023 work factor).
    /// Stored format: pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 key&gt;.
    /// Hashes written by older versions (unsalted SHA-256 hex) still verify and are reported
    /// as needing a rehash so they can be upgraded the next time the password is entered.
    /// </summary>
    public static class PasswordHasher
    {
        private const string Prefix = "pbkdf2-sha256";
        public const int Iterations = 600_000;
        private const int SaltSize = 16;
        private const int KeySize = 32;

        public static string Hash(string password)
        {
            ArgumentNullException.ThrowIfNull(password);
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        }

        public static bool IsLegacyHash(string storedHash)
        {
            return !string.IsNullOrEmpty(storedHash) && !storedHash.StartsWith(Prefix + "$", StringComparison.Ordinal);
        }

        public static bool Verify(string password, string storedHash, out bool needsRehash)
        {
            needsRehash = false;
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }
            if (IsLegacyHash(storedHash))
            {
                var legacy = Encoding.ASCII.GetBytes(StaticHelper.GetHash(password));
                var stored = Encoding.ASCII.GetBytes(storedHash.ToLowerInvariant());
                var matches = CryptographicOperations.FixedTimeEquals(legacy, stored);
                needsRehash = matches;
                return matches;
            }
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
            {
                return false;
            }
            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }
            if (expected.Length == 0)
            {
                return false;
            }
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            var ok = CryptographicOperations.FixedTimeEquals(actual, expected);
            needsRehash = ok && (iterations < Iterations || expected.Length != KeySize || salt.Length != SaltSize);
            return ok;
        }
    }
}
