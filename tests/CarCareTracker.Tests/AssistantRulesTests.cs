using CarCareTracker.Logic;
using CarCareTracker.Models;
using Xunit;

namespace CarCareTracker.Tests
{
    public class AssistantRulesTests
    {
        private static readonly List<string> Categories = new List<string> { "Ăn uống: Đi chợ", "Ăn uống: Ăn ngoài", "Xăng xe", "Quỹ khẩn cấp", "Other" };
        private static readonly List<string> Vehicles = new List<string> { "Honda", "Vision", "CX5" };
        private static AssistantRoute Route(string text) => AssistantRules.Route(text, Categories, Vehicles);

        [Theory]
        [InlineData("Tài khoản chung còn bao nhiêu?", AssistantTool.Balance)]
        [InlineData("số dư bây giờ", AssistantTool.Balance)]
        [InlineData("Tháng này chi bao nhiêu?", AssistantTool.CycleSummary)]
        [InlineData("tháng trước tiêu hết bao nhiêu", AssistantTool.CycleSummary)]
        [InlineData("Sắp tới phải trả gì?", AssistantTool.Upcoming)]
        [InlineData("bao giờ có lương", AssistantTool.Upcoming)]
        [InlineData("5 giao dịch gần nhất", AssistantTool.Recent)]
        [InlineData("tìm giao dịch grab", AssistantTool.Search)]
        [InlineData("đi chợ tháng này hết bao nhiêu", AssistantTool.Category)]
        [InlineData("xăng xe tháng này hết bao nhiêu", AssistantTool.Category)]
        [InlineData("đổ xăng 800k Vision", AssistantTool.Vehicle)]
        [InlineData("Hôm nay thay nhớt Vision ở 12.350km hết 120k", AssistantTool.Vehicle)]
        [InlineData("Bao giờ phải thay ắc quy?", AssistantTool.Vehicle)]
        [InlineData("bạn làm được gì", AssistantTool.Help)]
        [InlineData("kể chuyện cười đi", AssistantTool.Unknown)]
        public void PicksTheTool(string text, AssistantTool expected)
        {
            Assert.Equal(expected, Route(text).Tool);
        }

        [Fact]
        public void ReadsPeriodCountAndKeyword()
        {
            Assert.Equal(AssistantPeriod.Previous, Route("tháng trước chi bao nhiêu").Period);
            Assert.Equal(AssistantPeriod.Current, Route("tháng này chi bao nhiêu").Period);
            Assert.Equal(10, Route("10 giao dịch gần nhất").Count);
            Assert.Equal(AssistantRules.MaxCount, Route("99 giao dịch gần nhất").Count);
            Assert.Equal("grab", Route("Tìm giao dịch Grab").Keyword);
            Assert.Equal("Ăn uống: Đi chợ", Route("đi chợ hết bao nhiêu rồi").Category);
            Assert.Equal(TransferDirection.In, Route("tháng này tiền vào bao nhiêu").Direction);
        }

        [Fact]
        public void SanitizeDropsWhatTheModelMadeUp()
        {
            var route = AssistantRules.Sanitize(new AssistantRoute { Tool = AssistantTool.Category, Category = "Không có", Count = 500 }, Categories);
            Assert.Equal(AssistantTool.CycleSummary, route.Tool);
            Assert.Equal(string.Empty, route.Category);
            Assert.Equal(AssistantRules.MaxCount, route.Count);
            var search = AssistantRules.Sanitize(new AssistantRoute { Tool = AssistantTool.Search, Keyword = "!!!" }, Categories);
            Assert.Equal(AssistantTool.Recent, search.Tool);
            var longWord = AssistantRules.Sanitize(new AssistantRoute { Tool = AssistantTool.Search, Keyword = new string('a', 300) }, Categories);
            Assert.True(longWord.Keyword.Length <= AssistantRules.MaxKeywordLength);
        }

        [Fact]
        public void CycleRangesFollowThePayday()
        {
            var (from, to) = AssistantRules.PeriodRange(new DateOnly(2026, 11, 21), 5, AssistantPeriod.Current);
            Assert.Equal(new DateOnly(2026, 11, 5), from);
            Assert.Equal(new DateOnly(2026, 12, 4), to);
            var (prevFrom, prevTo) = AssistantRules.PeriodRange(new DateOnly(2026, 11, 21), 5, AssistantPeriod.Previous);
            Assert.Equal(new DateOnly(2026, 10, 5), prevFrom);
            Assert.Equal(new DateOnly(2026, 11, 4), prevTo);
            var (earlyFrom, _) = AssistantRules.PeriodRange(new DateOnly(2026, 11, 3), 5, AssistantPeriod.Current);
            Assert.Equal(new DateOnly(2026, 10, 5), earlyFrom);
        }

        private static BankTransaction Tx(TransferDirection direction, long amount, DateTime utc, string category = "", string content = "", string source = "api")
        {
            return new BankTransaction { Direction = direction, Amount = amount, OccurredAtUtc = utc, Category = category, Content = content, Source = source };
        }

        [Fact]
        public void TotalsSplitCategoriesAndUnrecordedSpending()
        {
            var list = new List<BankTransaction>
            {
                Tx(TransferDirection.In, 16_000_000, new DateTime(2026, 11, 5, 2, 0, 0, DateTimeKind.Utc)),
                Tx(TransferDirection.Out, 300_000, new DateTime(2026, 11, 6, 2, 0, 0, DateTimeKind.Utc), "Ăn uống: Đi chợ"),
                Tx(TransferDirection.Out, 200_000, new DateTime(2026, 11, 7, 2, 0, 0, DateTimeKind.Utc), "Ăn uống: Đi chợ"),
                Tx(TransferDirection.Out, 900_000, new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Utc), "Xăng xe"),
                Tx(TransferDirection.Out, 50_000, new DateTime(2026, 11, 9, 2, 0, 0, DateTimeKind.Utc)),
                Tx(TransferDirection.Out, 1_000_000, new DateTime(2026, 11, 10, 2, 0, 0, DateTimeKind.Utc), source: ReconciliationRules.SourceGap)
            };
            var totals = AssistantRules.Totals(list);
            Assert.Equal(16_000_000, totals.In);
            Assert.Equal(2_450_000, totals.Out);
            Assert.Equal(1_000_000, totals.UnrecordedOut);
            Assert.Equal(50_000, totals.Uncategorized);
            Assert.Equal(("Xăng xe", 900_000L), totals.ByCategory[0]);
            Assert.Equal(("Ăn uống: Đi chợ", 500_000L), totals.ByCategory[1]);
        }

        [Fact]
        public void InRangeUsesVietnamDates()
        {
            //00:30 on 5 Nov in Vietnam is 17:30 UTC on 4 Nov.
            var list = new List<BankTransaction>
            {
                Tx(TransferDirection.Out, 1, new DateTime(2026, 11, 4, 17, 30, 0, DateTimeKind.Utc)),
                Tx(TransferDirection.Out, 2, new DateTime(2026, 11, 4, 16, 30, 0, DateTimeKind.Utc))
            };
            var inRange = AssistantRules.InRange(list, new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 4));
            Assert.Single(inRange);
            Assert.Equal(1, inRange[0].Amount);
        }

        [Fact]
        public void SearchIgnoresAccentsAndGaps()
        {
            var list = new List<BankTransaction>
            {
                Tx(TransferDirection.Out, 1, new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), content: "Thanh toán GRAB đồ ăn"),
                Tx(TransferDirection.Out, 2, new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc), content: "Siêu thị"),
                Tx(TransferDirection.Out, 3, new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc), content: "grab", source: ReconciliationRules.SourceGap)
            };
            var found = AssistantRules.Search(list, "grab");
            Assert.Single(found);
            Assert.Equal(1, found[0].Amount);
            Assert.Single(AssistantRules.Search(list, "sieu thi"));
            Assert.Empty(AssistantRules.Search(list, " "));
        }

        [Fact]
        public void LimitsComeFromTheBudget()
        {
            var profile = new BudgetProfile
            {
                Funds = new List<Fund>
                {
                    new Fund { Name = "Ăn uống", MonthlyAmount = 6_000_000, Categories = new List<BudgetCategory> { new BudgetCategory { Name = "Đi chợ", MonthlyLimit = 4_000_000 }, new BudgetCategory { Name = "Ăn ngoài" } } },
                    new Fund { Name = "Xăng xe", MonthlyAmount = 1_500_000 }
                }
            };
            Assert.Equal(4_000_000, AssistantRules.MonthlyLimit(profile, "Ăn uống: Đi chợ"));
            Assert.Null(AssistantRules.MonthlyLimit(profile, "Ăn uống: Ăn ngoài"));
            Assert.Equal(1_500_000, AssistantRules.MonthlyLimit(profile, "Xăng xe"));
            Assert.Null(AssistantRules.MonthlyLimit(null, "Xăng xe"));
        }

        [Theory]
        [InlineData("food", true)]
        [InlineData("nhãn thực phẩm", true)]
        [InlineData("", false)]
        [InlineData("hóa đơn", false)]
        public void PhotoHint(string hint, bool food)
        {
            Assert.Equal(food, AssistantRules.WantsFoodLabel(hint));
        }

        [Fact]
        public void RouterSchemaOnlyAllowsKnownCategories()
        {
            var schema = AssistantLogic.BuildRouterSchema(Categories);
            var values = schema["properties"]!["category"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
            Assert.Contains(string.Empty, values);
            Assert.Contains("Xăng xe", values);
            Assert.Equal(Categories.Count + 1, values.Count);
            Assert.Contains("ignore", AssistantLogic.BuildRouterPrompt(Categories, new DateOnly(2026, 11, 21)).Replace("bỏ qua", "ignore"));
        }

        [Fact]
        public void MoneyIsVietnameseFormatted()
        {
            Assert.Equal("1.234.567 ₫", AssistantLogic.Money(1_234_567));
        }
    }
}
