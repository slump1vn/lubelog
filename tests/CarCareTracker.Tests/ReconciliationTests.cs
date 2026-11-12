using CarCareTracker.Logic;
using CarCareTracker.Models;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace CarCareTracker.Tests
{
    public class ReconciliationTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
        private static int nextId = 1;
        private static BankTransaction Tx(int minutes, TransferDirection direction, long amount, long? balance, string source = "webhook") => new BankTransaction
        {
            Id = nextId++,
            HouseholdId = -1,
            Fingerprint = Guid.NewGuid().ToString(),
            OccurredAtUtc = T0.AddMinutes(minutes),
            Direction = direction,
            Amount = amount,
            Balance = balance,
            Source = source
        };

        [Fact]
        public void NoGapWhenBalancesChain()
        {
            var list = new[]
            {
                Tx(0, TransferDirection.In, 2_000, 81_138),
                Tx(10, TransferDirection.In, 10_000, 91_138),
                Tx(20, TransferDirection.Out, 50_000, 41_138)
            };
            Assert.Empty(ReconciliationRules.FindGaps(list));
        }

        [Fact]
        public void FindsUnrecordedSpendingBetweenTwoBankLines()
        {
            var list = new[]
            {
                Tx(0, TransferDirection.In, 2_000, 81_138),
                //a 30,000 card payment SePay never reported, then a 10,000 SEVQR transfer in
                Tx(60, TransferDirection.In, 10_000, 61_138)
            };
            var gap = Assert.Single(ReconciliationRules.FindGaps(list));
            Assert.Equal(-30_000, gap.Amount);
            Assert.Equal(TransferDirection.Out, gap.Direction);
            Assert.Equal(81_138, gap.BalanceBefore);
            Assert.Equal(61_138, gap.BalanceAfter);
            Assert.Equal(T0, gap.FromUtc);
            Assert.Equal(T0.AddMinutes(60), gap.ToUtc);
        }

        [Fact]
        public void ManualEntriesInsideTheGapExplainIt()
        {
            var list = new List<BankTransaction>
            {
                Tx(0, TransferDirection.In, 2_000, 81_138),
                Tx(30, TransferDirection.Out, 20_000, null, ReconciliationRules.SourceManual),
                Tx(60, TransferDirection.In, 10_000, 61_138)
            };
            var gap = Assert.Single(ReconciliationRules.FindGaps(list));
            Assert.Equal(-10_000, gap.Amount);
            Assert.Equal(1, gap.ManualEntries);
            list.Add(Tx(40, TransferDirection.Out, 10_000, null, ReconciliationRules.SourceManual));
            Assert.Empty(ReconciliationRules.FindGaps(list));
        }

        [Fact]
        public void GapKeyIsStableAndTotalsCountFromCycleStart()
        {
            var a = Tx(0, TransferDirection.In, 2_000, 100_000);
            var b = Tx(60 * 24 * 3, TransferDirection.In, 1_000, 51_000);
            var key1 = ReconciliationRules.FindGaps(new[] { a, b }).Single().Key;
            var key2 = ReconciliationRules.FindGaps(new[] { b, a }).Single().Key;
            Assert.Equal(key1, key2);
            var gaps = ReconciliationRules.FindGaps(new[] { a, b });
            Assert.Equal((0L, 50_000L), ReconciliationRules.UnrecordedTotals(gaps, new DateOnly(2026, 11, 13)));
            Assert.Equal((0L, 0L), ReconciliationRules.UnrecordedTotals(gaps, new DateOnly(2026, 11, 16)));
        }

        [Fact]
        public void SameSecondTransactionsAreOrderedByTheirBalances()
        {
            var first = Tx(0, TransferDirection.In, 1_000, 101_000);
            var later = Tx(5, TransferDirection.Out, 30_000, 70_000);
            var sameSecondB = Tx(5, TransferDirection.Out, 1_000, 100_000);
            //stored in the wrong order (higher id first)
            later.Id = 2; sameSecondB.Id = 3;
            Assert.Empty(ReconciliationRules.FindGaps(new[] { first, later, sameSecondB }));
        }

        [Fact]
        public void WithGapsMakesDailyBalancesMatchTheBank()
        {
            var list = new[]
            {
                Tx(0, TransferDirection.In, 2_000, 100_000),
                Tx(60 * 30, TransferDirection.In, 10_000, 60_000)
            };
            var points = DashboardRules.GetDailyBalances(ReconciliationRules.WithGaps(list), 60_000, new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 13));
            Assert.Equal(100_000, points[0].Balance);
            Assert.Equal(60_000, points[1].Balance);
        }

        [Fact]
        public void ValidatesManualEntries()
        {
            Assert.Empty(ReconciliationRules.ValidateManual(10_000, T0, "Ăn uống", "phở", T0));
            Assert.NotEmpty(ReconciliationRules.ValidateManual(0, T0, null, null, T0));
            Assert.NotEmpty(ReconciliationRules.ValidateManual(1, T0.AddDays(3), null, null, T0));
            Assert.NotEmpty(ReconciliationRules.ValidateManual(1, T0, null, new string('x', 301), T0));
        }

        [Fact]
        public void SharedHouseholdPutsEveryoneInTheAdminHousehold()
        {
            Assert.Equal(-1, HouseholdRules.Resolve(5, true, new[] { 3 }));
            Assert.Equal(-1, HouseholdRules.Resolve(7, true, Array.Empty<int>()));
            Assert.Equal(3, HouseholdRules.Resolve(5, false, new[] { 9, 3 }));
            Assert.Equal(7, HouseholdRules.Resolve(7, false, Array.Empty<int>()));
        }
    }

    public class StatementTests
    {
        private const string VietinCsv =
            "NGÂN HÀNG TMCP CÔNG THƯƠNG VIỆT NAM\n" +
            "SAO KÊ TÀI KHOẢN\n" +
            "Số tài khoản: 888829328888,,,,,\n" +
            "STT,Ngày giao dịch,Số tham chiếu,Nội dung giao dịch,Ghi nợ,Ghi có,Số dư\n" +
            "1,12/11/2026 16:08:05,FT001,SEVQR chuyen khoan,,\"2,000\",\"81,138\"\n" +
            "2,12/11/2026 16:30:00,FT002,THANH TOAN THE CIRCLE K,\"30,000\",,\"51,138\"\n" +
            "3,12/11/2026 17:20:20,FT003,SEVQR nap tien,,\"10,000\",\"61,138\"\n" +
            ",Tổng cộng,,,\"30,000\",\"12,000\",\n";

        [Theory]
        [InlineData("1.234.567", 1234567)]
        [InlineData("1,234,567", 1234567)]
        [InlineData("50000.00", 50000)]
        [InlineData("50.000 VND", 50000)]
        [InlineData("-30,000", -30000)]
        [InlineData("(30.000)", -30000)]
        [InlineData("2000", 2000)]
        [InlineData("1.5", 2)]
        public void ParsesVietnameseAndEnglishAmounts(string text, long expected)
        {
            Assert.Equal(expected, StatementParser.ParseAmount(text));
        }

        [Fact]
        public void ParsesDatesIncludingExcelSerials()
        {
            var parsed = StatementParser.ParseDate("12/11/2026 16:08:05", out bool hasTime);
            Assert.True(hasTime);
            Assert.Equal(new DateTime(2026, 11, 12, 16, 8, 5), parsed);
            var dayOnly = StatementParser.ParseDate("12/11/2026", out hasTime);
            Assert.False(hasTime);
            Assert.Equal(new DateTime(2026, 11, 12, 12, 0, 0), dayOnly);
            var serial = new DateTime(2026, 11, 12, 16, 8, 5).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var fromSerial = StatementParser.ParseDate(serial, out hasTime)!.Value;
            Assert.True(hasTime);
            Assert.True(Math.Abs((fromSerial - new DateTime(2026, 11, 12, 16, 8, 5)).TotalSeconds) < 1);
            Assert.Null(StatementParser.ParseDate("Tổng cộng", out _));
        }

        [Fact]
        public void ReadsAVietinBankStyleCsv()
        {
            var result = StatementParser.Parse(Encoding.UTF8.GetBytes(VietinCsv), "saoke.csv");
            Assert.Empty(result.Errors);
            Assert.Equal("888829328888", result.AccountNumber);
            Assert.Equal(3, result.Rows.Count);
            Assert.Equal(1, result.SkippedLines);
            var card = result.Rows[1];
            Assert.Equal(TransferDirection.Out, card.Direction);
            Assert.Equal(30_000, card.Amount);
            Assert.Equal(51_138, card.Balance);
            Assert.Equal("FT002", card.Reference);
            Assert.Equal("THANH TOAN THE CIRCLE K", card.Description);
            Assert.Equal(new DateTime(2026, 11, 12, 9, 30, 0, DateTimeKind.Utc), card.OccurredAtUtc);
            Assert.Equal("Ghi nợ", result.Columns["debit"]);
        }

        [Fact]
        public void ReadsAnXlsxWithSharedStringsAndNumbers()
        {
            var bytes = BuildXlsx(new[]
            {
                new object[] { "Account number 888829328888" },
                new object[] { },
                new object[] { "Transaction date", "Description", "Debit", "Credit", "Balance" },
                new object[] { "12/11/2026 16:08:05", "SEVQR chuyen khoan", "", 2000, 81138 },
                new object[] { new DateTime(2026, 11, 12, 16, 30, 0).ToOADate(), "Card payment", 30000, "", 51138 }
            });
            var result = StatementParser.Parse(bytes, "statement.xlsx");
            Assert.Empty(result.Errors);
            Assert.Equal("888829328888", result.AccountNumber);
            Assert.Equal(2, result.Rows.Count);
            Assert.Equal(TransferDirection.Out, result.Rows[1].Direction);
            Assert.Equal(30_000, result.Rows[1].Amount);
            Assert.Equal(4, result.Rows[0].Line);
        }

        [Fact]
        public void RefusesPdfOldXlsAndTablesWithoutHeaders()
        {
            Assert.NotEmpty(StatementParser.Parse(Encoding.ASCII.GetBytes("%PDF-1.7 ..."), "a.pdf").Errors);
            Assert.NotEmpty(StatementParser.Parse(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0 }, "a.xls").Errors);
            Assert.NotEmpty(StatementParser.Parse(Encoding.UTF8.GetBytes("a,b,c\n1,2,3\n"), "a.csv").Errors);
            Assert.NotEmpty(StatementParser.Parse(Array.Empty<byte>(), "a.csv").Errors);
        }

        [Fact]
        public void ImportSkipsWhatSepayAlreadyHasAndReplacesManualEntries()
        {
            var rows = StatementParser.Parse(Encoding.UTF8.GetBytes(VietinCsv), "saoke.csv").Rows;
            var sepay = new BankTransaction { Id = 1, Source = "api", Direction = TransferDirection.In, Amount = 2_000, Balance = 81_138, OccurredAtUtc = new DateTime(2026, 11, 12, 9, 8, 5, DateTimeKind.Utc) };
            var webhook = new BankTransaction { Id = 2, Source = "webhook", Direction = TransferDirection.In, Amount = 10_000, Balance = 61_138, OccurredAtUtc = new DateTime(2026, 11, 12, 10, 20, 21, DateTimeKind.Utc) };
            var manual = new BankTransaction { Id = 3, Source = ReconciliationRules.SourceManual, Direction = TransferDirection.Out, Amount = 30_000, OccurredAtUtc = new DateTime(2026, 11, 12, 5, 0, 0, DateTimeKind.Utc), Category = "Xăng" };
            var plans = StatementImportRules.Plan(rows, new[] { sepay, webhook, manual }, -1, "888829328888");
            Assert.Equal(StatementRowOutcome.AlreadyRecorded, plans[0].Outcome);
            Assert.Equal(StatementRowOutcome.ReplacesManual, plans[1].Outcome);
            Assert.Same(manual, plans[1].Match);
            Assert.Equal(StatementRowOutcome.AlreadyRecorded, plans[2].Outcome);
            //importing into an empty account adds everything, and twice the same file gives the same fingerprints.
            var fresh = StatementImportRules.Plan(rows, Array.Empty<BankTransaction>(), -1, "888829328888");
            Assert.All(fresh, x => Assert.Equal(StatementRowOutcome.New, x.Outcome));
            Assert.Equal(StatementImportRules.ToTransaction(rows[0], -1, "888829328888", 1, DateTime.UtcNow).Fingerprint,
                StatementImportRules.ToTransaction(rows[0], -1, "888829328888", 2, DateTime.UtcNow).Fingerprint);
        }

        [Fact]
        public void EqualPaymentsTheSameDayAreToldApartByBalance()
        {
            var day = new DateTime(2026, 11, 12, 3, 0, 0, DateTimeKind.Utc);
            var first = new BankTransaction { Id = 1, Source = ReconciliationRules.SourceStatement, Direction = TransferDirection.Out, Amount = 50_000, Balance = 950_000, OccurredAtUtc = day };
            var second = new BankTransaction { Id = 2, Source = ReconciliationRules.SourceStatement, Direction = TransferDirection.Out, Amount = 50_000, Balance = 900_000, OccurredAtUtc = day.AddHours(2) };
            var fromSepay = new BankTransaction { Source = "webhook", Direction = TransferDirection.Out, Amount = 50_000, Balance = 900_000, OccurredAtUtc = day.AddHours(2).AddSeconds(3) };
            Assert.Same(second, StatementImportRules.FindCounterpart(fromSepay, new[] { first, second }));
            var other = new BankTransaction { Source = "webhook", Direction = TransferDirection.Out, Amount = 50_000, Balance = 850_000, OccurredAtUtc = day.AddHours(3) };
            Assert.Null(StatementImportRules.FindCounterpart(other, new[] { first, second }));
        }

        /// <summary>A minimal .xlsx: numbers as numbers, text as shared strings, like Excel writes it.</summary>
        private static byte[] BuildXlsx(object[][] rows)
        {
            var strings = new List<string>();
            var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (int r = 0; r < rows.Length; r++)
            {
                sheet.Append($"<row r=\"{r + 1}\">");
                for (int c = 0; c < rows[r].Length; c++)
                {
                    var reference = $"{(char)('A' + c)}{r + 1}";
                    var value = rows[r][c];
                    if (value is string s)
                    {
                        if (s.Length == 0) continue;
                        strings.Add(s);
                        sheet.Append($"<c r=\"{reference}\" t=\"s\"><v>{strings.Count - 1}</v></c>");
                    }
                    else
                    {
                        sheet.Append($"<c r=\"{reference}\"><v>{Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                    }
                }
                sheet.Append("</row>");
            }
            sheet.Append("</sheetData></worksheet>");
            var shared = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            foreach (var s in strings)
            {
                shared.Append("<si><t>").Append(System.Security.SecurityElement.Escape(s)).Append("</t></si>");
            }
            shared.Append("</sst>");
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                void Add(string name, string content)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                    writer.Write(content);
                }
                Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
                Add("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sao ke\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                Add("xl/worksheets/sheet1.xml", sheet.ToString());
                Add("xl/sharedStrings.xml", shared.ToString());
            }
            return stream.ToArray();
        }
    }
}
