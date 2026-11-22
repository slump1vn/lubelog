using CarCareTracker.Logic;
using CarCareTracker.Models;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CarCareTracker.Tests
{
    public class SepayRulesTests
    {
        private const string Secret = "test-secret";
        //the example body from https://developer.sepay.vn/vi/sepay-webhooks/tich-hop-webhook
        private const string WebhookBody = "{\"id\": 92704, \"gateway\": \"Vietcombank\", \"transactionDate\": \"2024-07-02 11:08:33\", \"accountNumber\": \"1017588888\", \"subAccount\": \"\", \"code\": null, \"content\": \"SEVN63DC8E5C chuyen tien\", \"transferType\": \"in\", \"description\": \"NGUYEN VAN A chuyen tien\", \"transferAmount\": 5000000, \"accumulated\": 105000000, \"referenceCode\": \"FT24012345678\"}";
        private static readonly DateTime Now = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);

        private static SepayWebhookPayload Payload() => JsonSerializer.Deserialize<SepayWebhookPayload>(WebhookBody)!;

        [Fact]
        public void AcceptsSignatureMadeTheWaySepayMakesIt()
        {
            var body = Encoding.UTF8.GetBytes(WebhookBody);
            long timestamp = 1_800_000_000;
            var signature = SepayRules.Sign(Secret, timestamp, body);
            Assert.StartsWith("sha256=", signature);
            Assert.True(SepayRules.VerifySignature(Secret, timestamp.ToString(), signature, body, timestamp + 10));
            Assert.True(SepayRules.VerifySignature(Secret, timestamp.ToString(), signature.ToUpperInvariant().Replace("SHA256=", "sha256="), body, timestamp));
        }

        [Fact]
        public void RefusesTamperedBodyWrongSecretAndMissingHeaders()
        {
            var body = Encoding.UTF8.GetBytes(WebhookBody);
            long timestamp = 1_800_000_000;
            var signature = SepayRules.Sign(Secret, timestamp, body);
            var tampered = Encoding.UTF8.GetBytes(WebhookBody.Replace("5000000", "9000000"));
            Assert.False(SepayRules.VerifySignature(Secret, timestamp.ToString(), signature, tampered, timestamp));
            Assert.False(SepayRules.VerifySignature("other", timestamp.ToString(), signature, body, timestamp));
            Assert.False(SepayRules.VerifySignature(Secret, null, signature, body, timestamp));
            Assert.False(SepayRules.VerifySignature(Secret, timestamp.ToString(), null, body, timestamp));
            Assert.False(SepayRules.VerifySignature(Secret, timestamp.ToString(), "sha256=zz", body, timestamp));
            Assert.False(SepayRules.VerifySignature(string.Empty, timestamp.ToString(), signature, body, timestamp));
        }

        [Theory]
        [InlineData(301)]
        [InlineData(-301)]
        public void RefusesSignaturesOutsideFiveMinutes(long skew)
        {
            var body = Encoding.UTF8.GetBytes(WebhookBody);
            long timestamp = 1_800_000_000;
            var signature = SepayRules.Sign(Secret, timestamp, body);
            Assert.False(SepayRules.VerifySignature(Secret, timestamp.ToString(), signature, body, timestamp + skew));
        }

        [Fact]
        public void ReadsVietnamTimeAsUtcPlusSeven()
        {
            Assert.Equal(new DateTime(2024, 7, 2, 4, 8, 33, DateTimeKind.Utc), SepayRules.ParseVietnamTime("2024-07-02 11:08:33"));
            Assert.Equal(new DateTime(2026, 3, 18, 2, 0, 0, DateTimeKind.Utc), SepayRules.ParseVietnamTime("2026-03-18T09:00:00+07:00"));
            Assert.Null(SepayRules.ParseVietnamTime("02/07/2024"));
            Assert.Null(SepayRules.ParseVietnamTime(null));
            Assert.Equal(new DateTime(2024, 7, 2, 11, 8, 33), SepayRules.ToVietnamTime(new DateTime(2024, 7, 2, 4, 8, 33, DateTimeKind.Utc)));
        }

        [Fact]
        public void ConvertsWebhookPayload()
        {
            var transaction = SepayRules.FromWebhook(Payload(), 1, Now)!;
            Assert.Equal(TransferDirection.In, transaction.Direction);
            Assert.Equal(5_000_000, transaction.Amount);
            Assert.Equal(105_000_000, transaction.Balance);
            Assert.Equal("1017588888", transaction.AccountNumber);
            Assert.Equal("FT24012345678", transaction.Reference);
            Assert.Equal(92704, transaction.SepayWebhookId);
            Assert.Equal("webhook", transaction.Source);
            Assert.NotEmpty(transaction.Fingerprint);
        }

        [Fact]
        public void UnknownBalanceIsNull()
        {
            var payload = Payload();
            payload.Accumulated = 0;
            Assert.Null(SepayRules.FromWebhook(payload, 1, Now)!.Balance);
        }

        [Fact]
        public void RejectsUnusableWebhookPayloads()
        {
            var badType = Payload(); badType.TransferType = "sideways";
            var badDate = Payload(); badDate.TransactionDate = "yesterday";
            var noAmount = Payload(); noAmount.TransferAmount = 0;
            Assert.Null(SepayRules.FromWebhook(badType, 1, Now));
            Assert.Null(SepayRules.FromWebhook(badDate, 1, Now));
            Assert.Null(SepayRules.FromWebhook(noAmount, 1, Now));
        }

        [Fact]
        public void WebhookAndApiCopiesOfOneTransactionShareAFingerprint()
        {
            var fromWebhook = SepayRules.FromWebhook(Payload(), 1, Now)!;
            var fromApi = SepayRules.FromApi(new SepayApiTransaction
            {
                Id = "1f0e2d3c-0000-4000-8000-000000000001",
                TransactionDate = "2024-07-02 11:08:33",
                AccountNumber = "1017588888",
                TransferType = "in",
                AmountIn = 5_000_000,
                Accumulated = 105_000_000,
                TransactionContent = "SEVN63DC8E5C chuyen tien",
                ReferenceNumber = "FT24012345678",
                BankBrandName = "Vietcombank"
            }, 1, Now)!;
            Assert.Equal(fromWebhook.Fingerprint, fromApi.Fingerprint);
            Assert.Equal("api", fromApi.Source);
        }

        [Fact]
        public void WithoutReferenceTimeAndContentTellTransactionsApart()
        {
            var first = Payload(); first.ReferenceCode = "";
            var sameAgain = Payload(); sameAgain.ReferenceCode = ""; sameAgain.Id = 99999; sameAgain.Content = "SEVN63DC8E5C   chuyen tien";
            var later = Payload(); later.ReferenceCode = ""; later.TransactionDate = "2024-07-02 11:09:00";
            var a = SepayRules.FromWebhook(first, 1, Now)!.Fingerprint;
            Assert.Equal(a, SepayRules.FromWebhook(sameAgain, 1, Now)!.Fingerprint);
            Assert.NotEqual(a, SepayRules.FromWebhook(later, 1, Now)!.Fingerprint);
        }

        [Fact]
        public void FingerprintDependsOnHouseholdAndDirection()
        {
            var payload = Payload();
            var a = SepayRules.FromWebhook(payload, 1, Now)!.Fingerprint;
            Assert.NotEqual(a, SepayRules.FromWebhook(payload, 2, Now)!.Fingerprint);
            payload.TransferType = "out";
            Assert.NotEqual(a, SepayRules.FromWebhook(payload, 1, Now)!.Fingerprint);
        }

        [Fact]
        public void ApiItemWithoutTransferTypeUsesAmounts()
        {
            var transaction = SepayRules.FromApi(new SepayApiTransaction
            {
                TransactionDate = "2026-11-10 08:00:00",
                AccountNumber = "1017588888",
                AmountOut = 250_000,
                TransactionContent = "di cho"
            }, 1, Now)!;
            Assert.Equal(TransferDirection.Out, transaction.Direction);
            Assert.Equal(250_000, transaction.Amount);
        }

        [Fact]
        public void TotalsCountFromTheCycleStartInVietnamTime()
        {
            DateTime At(string vietnamTime) => SepayRules.ParseVietnamTime(vietnamTime)!.Value;
            var transactions = new List<BankTransaction>
            {
                new BankTransaction { Direction = TransferDirection.In, Amount = 16_000_000, OccurredAtUtc = At("2026-11-20 00:30:00") },
                new BankTransaction { Direction = TransferDirection.Out, Amount = 400_000, OccurredAtUtc = At("2026-11-21 12:00:00") },
                new BankTransaction { Direction = TransferDirection.Out, Amount = 999_000, OccurredAtUtc = At("2026-11-19 23:59:59") }
            };
            var totals = SepayRules.Totals(transactions, new DateOnly(2026, 11, 20));
            Assert.Equal(16_000_000, totals.In);
            Assert.Equal(400_000, totals.Out);
        }

        [Theory]
        [InlineData("1017 5888 88", true)]
        [InlineData("", true)]
        [InlineData("12", false)]
        [InlineData("1017-588888", false)]
        public void ValidatesSharedAccountNumber(string accountNumber, bool valid)
        {
            var profile = BudgetRules.CreateFamilyTemplate(1, new List<(int, string)> { (1, "Sói"), (2, "Thỏ") });
            profile.SharedAccount = new SharedAccount { AccountNumber = accountNumber };
            var issues = BudgetRules.Validate(profile);
            Assert.Equal(valid, issues.Count == 0);
        }

        [Fact]
        public void MissingSharedAccountIsNormalized()
        {
            var profile = BudgetRules.CreateFamilyTemplate(1, new List<(int, string)> { (1, "Sói"), (2, "Thỏ") });
            profile.SharedAccount = null!;
            Assert.Empty(BudgetRules.Validate(profile));
            Assert.NotNull(profile.SharedAccount);
        }
    }
}
