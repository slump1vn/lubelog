using CarCareTracker.External.Interfaces;
using CarCareTracker.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CarCareTracker.Logic
{
    public enum WebhookResult
    {
        /// <summary>Accepted (stored, a duplicate, or not for any household here).</summary>
        Accepted,
        /// <summary>The webhook secret is not configured, so the endpoint is off.</summary>
        Disabled,
        Unauthorized,
        BadRequest
    }
    public interface ISepayLogic
    {
        bool ApiConfigured { get; }
        bool WebhookConfigured { get; }
        int SyncMinutes { get; }
        WebhookResult ReceiveWebhook(byte[] rawBody, string? timestampHeader, string? signatureHeader);
        Task<AccountSyncStatus> SyncHousehold(int householdId, CancellationToken cancellationToken);
        Task SyncAll(CancellationToken cancellationToken);
        AccountSyncStatus? GetLastSync(int householdId);
        SharedAccountViewModel GetAccountView(int householdId);
        FinanceDashboardViewModel GetDashboard(int householdId);
    }
    /// <summary>
    /// Watches the household's joint account through SePay. Two ways in, both stored through
    /// the same fingerprint so nothing is counted twice:
    /// - a signed webhook (needs a public HTTPS address, e.g. the Cloudflare tunnel), instant;
    /// - a periodic pull from the SePay API, which works from the home network and fills any
    ///   webhook that was missed.
    /// Keys come from the server configuration only.
    /// </summary>
    public class SepayLogic : ISepayLogic
    {
        private const string DefaultApiUrl = "https://userapi.sepay.vn/v2";
        //SePay allows 3 requests per second per IP.
        private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(400);
        private const int MaxPages = 50;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IBudgetDataAccess _budgetData;
        private readonly IBankTransactionDataAccess _transactionData;
        private readonly ILogger<SepayLogic> _logger;
        private readonly ConcurrentDictionary<int, AccountSyncStatus> _lastSync = new ConcurrentDictionary<int, AccountSyncStatus>();
        private readonly SemaphoreSlim _syncLock = new SemaphoreSlim(1, 1);
        public SepayLogic(IHttpClientFactory httpClientFactory, IConfiguration configuration, IBudgetDataAccess budgetData, IBankTransactionDataAccess transactionData, ILogger<SepayLogic> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _budgetData = budgetData;
            _transactionData = transactionData;
            _logger = logger;
        }
        private string ApiKey => (_configuration["LUBELOGGER_SEPAY_API_KEY"] ?? string.Empty).Trim();
        private string WebhookSecret => (_configuration["LUBELOGGER_SEPAY_WEBHOOK_SECRET"] ?? string.Empty).Trim();
        private string ApiUrl => (_configuration["LUBELOGGER_SEPAY_API_URL"] ?? DefaultApiUrl).Trim().TrimEnd('/');
        private int LookbackDays => int.TryParse(_configuration["LUBELOGGER_SEPAY_SYNC_DAYS"], out int days) && days > 0 ? days : 35;
        public bool ApiConfigured => !string.IsNullOrEmpty(ApiKey);
        public bool WebhookConfigured => !string.IsNullOrEmpty(WebhookSecret);
        public int SyncMinutes => !ApiConfigured ? 0 : int.TryParse(_configuration["LUBELOGGER_SEPAY_SYNC_MINUTES"], out int minutes) ? Math.Max(minutes, 0) : 15;

        public WebhookResult ReceiveWebhook(byte[] rawBody, string? timestampHeader, string? signatureHeader)
        {
            if (!WebhookConfigured)
            {
                return WebhookResult.Disabled;
            }
            if (!SepayRules.VerifySignature(WebhookSecret, timestampHeader, signatureHeader, rawBody, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            {
                _logger.LogWarning("SePay webhook refused: missing or invalid signature.");
                return WebhookResult.Unauthorized;
            }
            SepayWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<SepayWebhookPayload>(rawBody);
            }
            catch (JsonException)
            {
                return WebhookResult.BadRequest;
            }
            if (payload == null)
            {
                return WebhookResult.BadRequest;
            }
            var accountNumber = SepayRules.NormalizeAccountNumber(payload.AccountNumber);
            var households = _budgetData.GetBudgetProfiles()
                .Where(x => !string.IsNullOrEmpty(x.SharedAccount?.AccountNumber) && x.SharedAccount.AccountNumber == accountNumber)
                .Select(x => x.Id)
                .ToList();
            if (!households.Any())
            {
                //acknowledge so SePay does not keep retrying a transaction of another account.
                _logger.LogInformation("SePay webhook {Id} is for an account no household watches; ignored.", payload.Id);
                return WebhookResult.Accepted;
            }
            foreach (var householdId in households)
            {
                var transaction = SepayRules.FromWebhook(payload, householdId, DateTime.UtcNow);
                if (transaction == null)
                {
                    _logger.LogWarning("SePay webhook {Id} could not be read as a transaction; ignored.", payload.Id);
                    return WebhookResult.Accepted;
                }
                _transactionData.AddIfNew(transaction);
            }
            return WebhookResult.Accepted;
        }

        public async Task SyncAll(CancellationToken cancellationToken)
        {
            foreach (var profile in _budgetData.GetBudgetProfiles().Where(x => !string.IsNullOrEmpty(x.SharedAccount?.AccountNumber)))
            {
                await SyncHousehold(profile.Id, cancellationToken);
            }
        }
        public async Task<AccountSyncStatus> SyncHousehold(int householdId, CancellationToken cancellationToken)
        {
            var status = new AccountSyncStatus { LastRunUtc = DateTime.UtcNow };
            var accountNumber = _budgetData.GetBudgetProfile(householdId)?.SharedAccount?.AccountNumber ?? string.Empty;
            if (!ApiConfigured)
            {
                status.Error = "The SePay API key is not configured";
                return status;
            }
            if (string.IsNullOrEmpty(accountNumber))
            {
                status.Error = "No shared account number is saved";
                return status;
            }
            await _syncLock.WaitAsync(cancellationToken);
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                var accounts = await GetPage<SepayApiBankAccount>(client, $"{ApiUrl}/bank-accounts?q={Uri.EscapeDataString(accountNumber)}&per_page=100", cancellationToken);
                var bankAccount = accounts.Data?.FirstOrDefault(x => SepayRules.NormalizeAccountNumber(x.AccountNumber) == accountNumber);
                if (bankAccount == null || string.IsNullOrEmpty(bankAccount.Id))
                {
                    status.Error = "SePay does not list this account number";
                    return status;
                }
                var latest = _transactionData.GetRecent(householdId, 1).FirstOrDefault();
                //look back two days past the newest stored transaction: banks sometimes report late.
                var fromUtc = latest == null ? DateTime.UtcNow.AddDays(-LookbackDays) : latest.OccurredAtUtc.AddDays(-2);
                var from = SepayRules.ToVietnamTime(fromUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                for (int page = 1; page <= MaxPages; page++)
                {
                    await Task.Delay(RequestSpacing, cancellationToken);
                    var url = $"{ApiUrl}/transactions?bank_account_id={Uri.EscapeDataString(bankAccount.Id)}&transaction_date_from={from}&transaction_date_sort=asc&per_page=100&page={page}";
                    var result = await GetPage<SepayApiTransaction>(client, url, cancellationToken);
                    foreach (var item in result.Data ?? new List<SepayApiTransaction>())
                    {
                        if (SepayRules.NormalizeAccountNumber(item.AccountNumber) != accountNumber)
                        {
                            continue;
                        }
                        var transaction = SepayRules.FromApi(item, householdId, DateTime.UtcNow);
                        if (transaction != null && _transactionData.AddIfNew(transaction))
                        {
                            status.Added++;
                        }
                    }
                    var pagination = result.Meta?.Pagination;
                    if (pagination == null || !pagination.HasMore || (result.Data?.Count ?? 0) == 0)
                    {
                        break;
                    }
                }
                status.Succeeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("SePay sync failed for household {HouseholdId}: {Message}", householdId, ex.Message);
                status.Error = ex is SepayApiException ? ex.Message : "Could not reach SePay";
            }
            finally
            {
                _lastSync[householdId] = status;
                _syncLock.Release();
            }
            return status;
        }
        private async Task<SepayListResponse<T>> GetPage<T>(HttpClient client, string url, CancellationToken cancellationToken)
        {
            for (int attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await client.SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    await Task.Delay(wait > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : wait, cancellationToken);
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new SepayApiException("SePay refused the API key");
                }
                if (!response.IsSuccessStatusCode)
                {
                    throw new SepayApiException($"SePay answered {(int)response.StatusCode}");
                }
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonSerializer.DeserializeAsync<SepayListResponse<T>>(stream, cancellationToken: cancellationToken) ?? new SepayListResponse<T>();
            }
        }
        public AccountSyncStatus? GetLastSync(int householdId)
        {
            return _lastSync.TryGetValue(householdId, out var status) ? status : null;
        }
        public SharedAccountViewModel GetAccountView(int householdId)
        {
            var profile = _budgetData.GetBudgetProfile(householdId);
            var today = DateOnly.FromDateTime(SepayRules.ToVietnamTime(DateTime.UtcNow));
            var cycleStart = BudgetRules.GetCycleStart(today, profile?.CycleStartDay ?? 1);
            var fromUtc = DateTime.SpecifyKind(cycleStart.ToDateTime(TimeOnly.MinValue) - SepayRules.VietnamOffset, DateTimeKind.Utc);
            var totals = SepayRules.Totals(_transactionData.GetSince(householdId, fromUtc), cycleStart);
            var recent = _transactionData.GetRecent(householdId, 30);
            return new SharedAccountViewModel
            {
                Account = profile?.SharedAccount ?? new SharedAccount(),
                ApiConfigured = ApiConfigured,
                WebhookConfigured = WebhookConfigured,
                SyncMinutes = SyncMinutes,
                LastSync = GetLastSync(householdId),
                Balance = recent.FirstOrDefault(x => x.Balance.HasValue)?.Balance,
                CycleStart = cycleStart,
                CycleIn = totals.In,
                CycleOut = totals.Out,
                Recent = recent
            };
        }
        public FinanceDashboardViewModel GetDashboard(int householdId)
        {
            var account = GetAccountView(householdId);
            var profile = _budgetData.GetBudgetProfile(householdId);
            var today = DateOnly.FromDateTime(SepayRules.ToVietnamTime(DateTime.UtcNow));
            var cycleEnd = DashboardRules.GetCycleEnd(account.CycleStart, profile?.CycleStartDay ?? 1);
            int dayOfCycle = today.DayNumber - account.CycleStart.DayNumber + 1;
            int daysInCycle = cycleEnd.DayNumber - account.CycleStart.DayNumber + 1;
            var fromUtc = DateTime.SpecifyKind(account.CycleStart.ToDateTime(TimeOnly.MinValue) - SepayRules.VietnamOffset, DateTimeKind.Utc);
            return new FinanceDashboardViewModel
            {
                Account = account,
                HasProfile = profile != null,
                Today = today,
                CycleEnd = cycleEnd,
                DayOfCycle = dayOfCycle,
                DaysInCycle = daysInCycle,
                ProjectedOut = DashboardRules.ProjectCycleOut(account.CycleOut, dayOfCycle, daysInCycle),
                BalanceHistory = DashboardRules.GetDailyBalances(_transactionData.GetSince(householdId, fromUtc), account.Balance, account.CycleStart, today),
                //the rest of this cycle plus the first days of the next, so the next payday always shows.
                Upcoming = DashboardRules.GetUpcoming(profile, today, cycleEnd.AddDays(7))
            };
        }
    }
    /// <summary>An error from SePay whose message is safe to show (it never contains the key).</summary>
    public class SepayApiException : Exception
    {
        public SepayApiException(string message) : base(message) { }
    }
    /// <summary>Pulls new transactions on a timer when the API key is configured.</summary>
    public class SepaySyncService : BackgroundService
    {
        private readonly ISepayLogic _sepayLogic;
        private readonly ILogger<SepaySyncService> _logger;
        public SepaySyncService(ISepayLogic sepayLogic, ILogger<SepaySyncService> logger)
        {
            _sepayLogic = sepayLogic;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var minutes = _sepayLogic.SyncMinutes;
            if (minutes <= 0)
            {
                return;
            }
            _logger.LogInformation("SePay sync every {Minutes} minutes.", minutes);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
                do
                {
                    try
                    {
                        await _sepayLogic.SyncAll(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError("SePay sync run failed: {Message}", ex.Message);
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                //stopping
            }
        }
    }
}
