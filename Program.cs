using CarCareTracker.External.Implementations;
using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Logic;
using CarCareTracker.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

//Additional JsonFile
builder.Configuration.AddJsonFile(StaticHelper.UserConfigPath, optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile(StaticHelper.ServerConfigPath, optional: true, reloadOnChange: true);
if (!string.IsNullOrWhiteSpace(builder.Configuration["LUBELOGGER_SECRETS_PATH"]))
{
    builder.Configuration.AddKeyPerFile(builder.Configuration["LUBELOGGER_SECRETS_PATH"] ?? string.Empty, optional: true);
}

if (!string.IsNullOrWhiteSpace(builder.Configuration["LUBELOGGER_LOCALE_OVERRIDE"]))
{
    var overrideCulture = new CultureInfo(builder.Configuration["LUBELOGGER_LOCALE_OVERRIDE"] ?? string.Empty);
    if (!string.IsNullOrWhiteSpace(builder.Configuration["LUBELOGGER_LOCALE_DT_OVERRIDE"]))
    {
        var overrideDTFormat = new CultureInfo(builder.Configuration["LUBELOGGER_LOCALE_DT_OVERRIDE"] ?? string.Empty);
        overrideCulture.DateTimeFormat = overrideDTFormat.DateTimeFormat;
    }
    CultureInfo.DefaultThreadCurrentCulture = overrideCulture;
    CultureInfo.DefaultThreadCurrentUICulture = overrideCulture;
}

//Print Messages
StaticHelper.InitMessage(builder.Configuration);
//Check Migration
StaticHelper.CheckMigration(builder.Environment.WebRootPath, builder.Environment.ContentRootPath);
//Install or update bundled translations
StaticHelper.SeedBundledTranslations(builder.Environment.WebRootPath, builder.Environment.ContentRootPath);

// Add services to the container.
builder.Services.AddControllersWithViews();

//LiteDB is always injected even if user uses Postgres.
builder.Services.AddSingleton<ILiteDBHelper, LiteDBHelper>();

//data access method
if (!string.IsNullOrWhiteSpace(builder.Configuration["POSTGRES_CONNECTION"])){
    builder.Services.AddSingleton<IVehicleDataAccess, PGVehicleDataAccess>();
    builder.Services.AddSingleton<INoteDataAccess, PGNoteDataAccess>();
    builder.Services.AddSingleton<IServiceRecordDataAccess, PGServiceRecordDataAccess>();
    builder.Services.AddSingleton<IGasRecordDataAccess, PGGasRecordDataAccess>();
    builder.Services.AddSingleton<ICollisionRecordDataAccess, PGCollisionRecordDataAccess>();
    builder.Services.AddSingleton<ITaxRecordDataAccess, PGTaxRecordDataAccess>();
    builder.Services.AddSingleton<IReminderRecordDataAccess, PGReminderRecordDataAccess>();
    builder.Services.AddSingleton<IUpgradeRecordDataAccess, PGUpgradeRecordDataAccess>();
    builder.Services.AddSingleton<IOdometerRecordDataAccess, PGOdometerRecordDataAccess>();
    builder.Services.AddSingleton<ISupplyRecordDataAccess, PGSupplyRecordDataAccess>();
    builder.Services.AddSingleton<IPlanRecordDataAccess, PGPlanRecordDataAccess>();
    builder.Services.AddSingleton<IPlanRecordTemplateDataAccess, PGPlanRecordTemplateDataAccess>();
    builder.Services.AddSingleton<IUserConfigDataAccess, PGUserConfigDataAccess>();
    builder.Services.AddSingleton<IUserRecordDataAccess, PGUserRecordDataAccess>();
    builder.Services.AddSingleton<ITokenRecordDataAccess, PGTokenRecordDataAccess>();
    builder.Services.AddSingleton<IUserAccessDataAccess, PGUserAccessDataAccess>();
    builder.Services.AddSingleton<IExtraFieldDataAccess, PGExtraFieldDataAccess>();
    builder.Services.AddSingleton<IInspectionRecordDataAccess, PGInspectionRecordDataAccess>();
    builder.Services.AddSingleton<IInspectionRecordTemplateDataAccess, PGInspectionRecordTemplateDataAccess>();
    builder.Services.AddSingleton<IEquipmentRecordDataAccess, PGEquipmentRecordDataAccess>();
    builder.Services.AddSingleton<IUserHouseholdDataAccess, PGUserHouseholdDataAccess>();
    builder.Services.AddSingleton<IApiKeyRecordDataAccess, PGApiKeyRecordDataAccess>();
    builder.Services.AddSingleton<IBudgetDataAccess, PGBudgetDataAccess>();
    builder.Services.AddSingleton<IBankTransactionDataAccess, PGBankTransactionDataAccess>();
    builder.Services.AddSingleton<IDBHealthCheck, PGDBHealthCheck>();
}
else
{
    builder.Services.AddSingleton<IVehicleDataAccess, VehicleDataAccess>();
    builder.Services.AddSingleton<INoteDataAccess, NoteDataAccess>();
    builder.Services.AddSingleton<IServiceRecordDataAccess, ServiceRecordDataAccess>();
    builder.Services.AddSingleton<IGasRecordDataAccess, GasRecordDataAccess>();
    builder.Services.AddSingleton<ICollisionRecordDataAccess, CollisionRecordDataAccess>();
    builder.Services.AddSingleton<ITaxRecordDataAccess, TaxRecordDataAccess>();
    builder.Services.AddSingleton<IReminderRecordDataAccess, ReminderRecordDataAccess>();
    builder.Services.AddSingleton<IUpgradeRecordDataAccess, UpgradeRecordDataAccess>();
    builder.Services.AddSingleton<IOdometerRecordDataAccess, OdometerRecordDataAccess>();
    builder.Services.AddSingleton<ISupplyRecordDataAccess, SupplyRecordDataAccess>();
    builder.Services.AddSingleton<IPlanRecordDataAccess, PlanRecordDataAccess>();
    builder.Services.AddSingleton<IPlanRecordTemplateDataAccess, PlanRecordTemplateDataAccess>();
    builder.Services.AddSingleton<IUserConfigDataAccess, UserConfigDataAccess>();
    builder.Services.AddSingleton<IUserRecordDataAccess, UserRecordDataAccess>();
    builder.Services.AddSingleton<ITokenRecordDataAccess, TokenRecordDataAccess>();
    builder.Services.AddSingleton<IUserAccessDataAccess, UserAccessDataAccess>();
    builder.Services.AddSingleton<IExtraFieldDataAccess, ExtraFieldDataAccess>();
    builder.Services.AddSingleton<IInspectionRecordDataAccess, InspectionRecordDataAccess>();
    builder.Services.AddSingleton<IInspectionRecordTemplateDataAccess, InspectionRecordTemplateDataAccess>();
    builder.Services.AddSingleton<IEquipmentRecordDataAccess, EquipmentRecordDataAccess>();
    builder.Services.AddSingleton<IUserHouseholdDataAccess, UserHouseholdDataAccess>();
    builder.Services.AddSingleton<IApiKeyRecordDataAccess, ApiKeyRecordDataAccess>();
    builder.Services.AddSingleton<IBudgetDataAccess, BudgetDataAccess>();
    builder.Services.AddSingleton<IBankTransactionDataAccess, BankTransactionDataAccess>();
    builder.Services.AddSingleton<IDBHealthCheck, DBHealthCheck>();
}

//configure helpers
builder.Services.AddSingleton<IFileHelper, FileHelper>();
builder.Services.AddSingleton<IGasHelper, GasHelper>();
builder.Services.AddSingleton<IEquipmentHelper, EquipmentHelper>();
builder.Services.AddSingleton<IReminderHelper, ReminderHelper>();
builder.Services.AddSingleton<IReportHelper, ReportHelper>();
builder.Services.AddSingleton<IConfigHelper, ConfigHelper>();
builder.Services.AddSingleton<ITranslationHelper, TranslationHelper>();
builder.Services.AddSingleton<IMailHelper, MailHelper>();

//configure logic
builder.Services.AddSingleton<ILoginLogic, LoginLogic>();
builder.Services.AddSingleton<IUserLogic, UserLogic>();
builder.Services.AddSingleton<IOdometerLogic, OdometerLogic>();
builder.Services.AddSingleton<IVehicleLogic, VehicleLogic>();
builder.Services.AddSingleton<INotificationLogic, NotificationLogic>();
builder.Services.AddSingleton<IMaintenanceLogic, MaintenanceLogic>();
builder.Services.AddSingleton<IAiLogic, AiLogic>();
builder.Services.AddSingleton<IBudgetLogic, BudgetLogic>();
builder.Services.AddSingleton<ISepayLogic, SepayLogic>();
//pulls the joint account from SePay when LUBELOGGER_SEPAY_API_KEY is set; does nothing otherwise.
builder.Services.AddHostedService<SepaySyncService>();

//configure automated event logic
if (StaticHelper.CheckConfigBoolean(builder.Configuration, "LUBELOGGER_AUTO_EVENTS"))
{
    builder.Services.AddHostedService<AutomatedEventLogic>();
}

//configure signalr
builder.Services.AddSingleton<IEventLogic, EventLogic>();
builder.Services.AddSignalR();

//configure Auth
builder.Services.AddHttpClient();
builder.Services.AddDataProtection();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication("AuthN").AddScheme<AuthenticationSchemeOptions, Authen>("AuthN", opts => { });
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder().AddAuthenticationSchemes("AuthN").RequireAuthenticatedUser().Build();
});
//configure max file upload size
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = int.MaxValue; // if don't set default value is: 30 MB
});
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = int.MaxValue; // if don't set default value is: 128 MB
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

//compress CSS/JS/fonts (Brotli, then gzip). Pages and JSON stay uncompressed, see StaticAssetCaching.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = StaticAssetCaching.CompressedMimeTypes;
});

var app = builder.Build();

//family defaults (equipment extra fields for batteries and tires), applied once.
if (StaticHelper.CheckConfigBoolean(builder.Configuration, "LUBELOGGER_FAMILY_DEFAULTS"))
{
    StaticHelper.SeedFamilyDefaults(builder.Environment.ContentRootPath, app.Services.GetRequiredService<IExtraFieldDataAccess>());
}

//configure the HTTP request pipeline.
app.UseExceptionHandler("/Error");
app.UseStatusCodePagesWithReExecute("/Error/{0}");

//static files: compressed, and cached by the browser according to StaticAssetCaching.
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = StaticAssetCaching.GetCacheControl(ctx.Context.Request.Path, ctx.Context.Request.Query);
    }
});

//api middleware
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.ContentType == "application/json",
    ab => ab.UseMiddleware<BufferBody>()
);

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

//signalr
app.MapHub<EventHubLogic>("/api/ws");

//Digital Asset Links: lets the Android app (android/) open this site full screen as a Trusted Web Activity.
//Set LUBELOGGER_ANDROID_CERT_SHA256 to the signing certificate fingerprint(s), comma separated. Empty list when unset.
app.MapGet("/.well-known/assetlinks.json", (IConfiguration config) =>
{
    var packageName = config["LUBELOGGER_ANDROID_PACKAGE"];
    if (string.IsNullOrWhiteSpace(packageName))
    {
        packageName = "vn.digifusion.finance";
    }
    var fingerprints = (config["LUBELOGGER_ANDROID_CERT_SHA256"] ?? string.Empty)
        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => x.ToUpperInvariant())
        .ToArray();
    if (!fingerprints.Any())
    {
        return Results.Json(Array.Empty<object>());
    }
    return Results.Json(new[]
    {
        new
        {
            relation = new[] { "delegate_permission/common.handle_all_urls" },
            target = new { @namespace = "android_app", package_name = packageName.Trim(), sha256_cert_fingerprints = fingerprints }
        }
    });
});

app.Run();
