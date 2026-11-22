using CarCareTracker.Models.Api;
using System.Text.Json;
using Xunit;

namespace CarCareTracker.Tests
{
    /// <summary>The published OpenAPI file must list every field the API v1 classes send.</summary>
    public class OpenApiContractTests
    {
        private static JsonElement Schemas()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CarCareTracker.csproj")))
            {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            var json = File.ReadAllText(Path.Combine(dir!.FullName, "wwwroot", "api", "openapi-v1.json"));
            return JsonDocument.Parse(json).RootElement.GetProperty("components").GetProperty("schemas");
        }

        [Theory]
        [InlineData(typeof(MeDto), "Me")]
        [InlineData(typeof(AccountDto), "Account")]
        [InlineData(typeof(CycleDto), "Cycle")]
        [InlineData(typeof(BalancePointDto), "BalancePoint")]
        [InlineData(typeof(UpcomingDto), "Upcoming")]
        [InlineData(typeof(TransactionDto), "Transaction")]
        [InlineData(typeof(DashboardDto), "Dashboard")]
        [InlineData(typeof(TransactionPageDto), "TransactionPage")]
        public void EveryFieldIsDocumented(Type dto, string schemaName)
        {
            var documented = Schemas().GetProperty(schemaName).GetProperty("properties").EnumerateObject().Select(x => x.Name).ToHashSet();
            var sent = dto.GetProperties().Select(x => JsonNamingPolicy.CamelCase.ConvertName(x.Name)).ToHashSet();
            Assert.Equal(sent.OrderBy(x => x), documented.OrderBy(x => x));
        }
    }
}
