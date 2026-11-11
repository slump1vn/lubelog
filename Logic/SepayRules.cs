using CarCareTracker.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CarCareTracker.Logic
{
    /// <summary>
    /// Pure helpers for SePay data: webhook signature checks, time conversion and turning
    /// webhook or API items into one <see cref="BankTransaction"/> shape with a shared
    /// fingerprint, so the same bank transaction is stored once whichever way it arrives.
    /// See https://developer.sepay.vn/vi/sepay-webhooks/xac-thuc and .../sepay-api/v2.
    /// </summary>
    public static class SepayRules
    {
        /// <summary>SePay reports times in Vietnam time, which has no daylight saving.</summary>
        public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
        /// <summary>Signed requests older or newer than this are refused, as SePay recommends.</summary>
        public const int SignatureToleranceSeconds = 300;

        /// <summary>
        /// Checks an HMAC-SHA256 webhook signature: header "X-SePay-Signature: sha256={hex}"
        /// over "{X-SePay-Timestamp}.{raw body}" with the webhook secret. Compares in constant time.
        /// </summary>
        public static bool VerifySignature(string secret, string? timestampHeader, string? signatureHeader, byte[] rawBody, long nowUnixSeconds)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(timestampHeader) || string.IsNullOrWhiteSpace(signatureHeader))
            {
                return false;
            }
            if (!long.TryParse(timestampHeader.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long timestamp)
                || Math.Abs(nowUnixSeconds - timestamp) > SignatureToleranceSeconds)
            {
                return false;
            }
            var signature = signatureHeader.Trim();
            const string prefix = "sha256=";
            if (!signature.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            byte[] given;
            try
            {
                given = Convert.FromHexString(signature.Substring(prefix.Length));
            }
            catch (FormatException)
            {
                return false;
            }
            var signed = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".").Concat(rawBody).ToArray();
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
            return CryptographicOperations.FixedTimeEquals(expected, given);
        }
        /// <summary>Hex signature for a body, as SePay would send it. Used by tests and tools.</summary>
        public static string Sign(string secret, long timestamp, byte[] rawBody)
        {
            var signed = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".").Concat(rawBody).ToArray();
            return "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed)).ToLowerInvariant();
        }
        /// <summary>Parses "yyyy-MM-dd HH:mm:ss" (Vietnam time) or an ISO 8601 time with offset into UTC.</summary>
        public static DateTime? ParseVietnamTime(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            text = text.Trim();
            if (DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                return DateTime.SpecifyKind(local - VietnamOffset, DateTimeKind.Utc);
            }
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset) && text.Contains('T'))
            {
                return withOffset.UtcDateTime;
            }
            return null;
        }
        public static DateTime ToVietnamTime(DateTime utc)
        {
            var value = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return DateTime.SpecifyKind(value + VietnamOffset, DateTimeKind.Unspecified);
        }
        public static string NormalizeAccountNumber(string? accountNumber)
        {
            return (accountNumber ?? string.Empty).Replace(" ", string.Empty).Trim();
        }
        /// <summary>
        /// Key shared by the webhook and the API copy of one bank transaction. The bank's
        /// reference number identifies it when present; otherwise time, amount and content do.
        /// </summary>
        public static string Fingerprint(int householdId, string accountNumber, TransferDirection direction, long amount, DateTime occurredAtUtc, string reference, string content)
        {
            var parts = string.IsNullOrWhiteSpace(reference)
                ? $"t|{occurredAtUtc:yyyy-MM-ddTHH:mm:ss}|{CollapseSpaces(content)}"
                : $"r|{reference.Trim()}";
            var text = $"{householdId}|{NormalizeAccountNumber(accountNumber)}|{(direction == TransferDirection.In ? "in" : "out")}|{amount}|{parts}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        }
        /// <summary>Returns null when the webhook body is not a usable transaction.</summary>
        public static BankTransaction? FromWebhook(SepayWebhookPayload payload, int householdId, DateTime nowUtc)
        {
            var direction = ParseDirection(payload.TransferType);
            var occurred = ParseVietnamTime(payload.TransactionDate);
            if (direction == null || occurred == null || payload.TransferAmount <= 0)
            {
                return null;
            }
            var transaction = new BankTransaction
            {
                HouseholdId = householdId,
                AccountNumber = NormalizeAccountNumber(payload.AccountNumber),
                Bank = payload.Gateway ?? string.Empty,
                Direction = direction.Value,
                Amount = payload.TransferAmount,
                Balance = payload.Accumulated > 0 ? payload.Accumulated : null,
                OccurredAtUtc = occurred.Value,
                Content = payload.Content ?? string.Empty,
                Reference = payload.ReferenceCode ?? string.Empty,
                SepayWebhookId = payload.Id,
                Source = "webhook",
                ImportedAtUtc = nowUtc
            };
            transaction.Fingerprint = Fingerprint(householdId, transaction.AccountNumber, transaction.Direction, transaction.Amount, transaction.OccurredAtUtc, transaction.Reference, transaction.Content);
            return transaction;
        }
        /// <summary>Returns null when the API item is not a usable transaction.</summary>
        public static BankTransaction? FromApi(SepayApiTransaction item, int householdId, DateTime nowUtc)
        {
            var direction = ParseDirection(item.TransferType);
            if (direction == null)
            {
                direction = item.AmountIn > 0 ? TransferDirection.In : item.AmountOut > 0 ? TransferDirection.Out : null;
            }
            var occurred = ParseVietnamTime(item.TransactionDate);
            var amount = direction == TransferDirection.In ? item.AmountIn : item.AmountOut;
            if (direction == null || occurred == null || amount <= 0)
            {
                return null;
            }
            var transaction = new BankTransaction
            {
                HouseholdId = householdId,
                AccountNumber = NormalizeAccountNumber(item.AccountNumber),
                Bank = item.BankBrandName ?? string.Empty,
                Direction = direction.Value,
                Amount = amount,
                Balance = item.Accumulated > 0 ? item.Accumulated : null,
                OccurredAtUtc = occurred.Value,
                Content = item.TransactionContent ?? string.Empty,
                Reference = item.ReferenceNumber ?? string.Empty,
                SepayApiId = item.Id ?? string.Empty,
                Source = "api",
                ImportedAtUtc = nowUtc
            };
            transaction.Fingerprint = Fingerprint(householdId, transaction.AccountNumber, transaction.Direction, transaction.Amount, transaction.OccurredAtUtc, transaction.Reference, transaction.Content);
            return transaction;
        }
        /// <summary>Money in and out of the account from the start date (inclusive, Vietnam time).</summary>
        public static (long In, long Out) Totals(IEnumerable<BankTransaction> transactions, DateOnly fromVietnamDate)
        {
            var fromUtc = DateTime.SpecifyKind(fromVietnamDate.ToDateTime(TimeOnly.MinValue) - VietnamOffset, DateTimeKind.Utc);
            long totalIn = 0, totalOut = 0;
            foreach (var transaction in transactions.Where(x => x.OccurredAtUtc >= fromUtc))
            {
                if (transaction.Direction == TransferDirection.In)
                {
                    totalIn += transaction.Amount;
                }
                else
                {
                    totalOut += transaction.Amount;
                }
            }
            return (totalIn, totalOut);
        }
        private static TransferDirection? ParseDirection(string? transferType)
        {
            return (transferType ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "in" => TransferDirection.In,
                "out" => TransferDirection.Out,
                _ => null
            };
        }
        private static string CollapseSpaces(string text)
        {
            return string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
