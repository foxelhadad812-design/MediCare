using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Repositories;
using MediCare.Data.Seed;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Contracts;
using MediCare.Services.Common;
using MediCare.Services.Extensions;
using MediCare.Web.Hubs;
using MediCare.Web.Infrastructure;
using MediCare.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Connection String from configuration / user-secrets
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured outside Development.");
    }

    connectionString = "Server=(localdb)\\mssqllocaldb;Database=MediCareDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, b => b.MigrationsAssembly("MediCare.Data")));

// Identity Configuration
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 8;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedAccount = false;

    // Account Lockout Protection (Brute-Force Defense)
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Cookie Security Configuration (SameAsRequest in Development for local HTTP, Always in Production)
builder.Services.ConfigureSecurityCookies(builder.Environment.IsDevelopment());

// Smtp Configuration
builder.Services.Configure<MediCare.Services.Common.SmtpSettings>(
    builder.Configuration.GetSection(MediCare.Services.Common.SmtpSettings.SectionName));

// Data Access & Unit of Work DI
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IDoctorRepository, DoctorRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IMedicalRecordRepository, MedicalRecordRepository>();
builder.Services.AddScoped<IPrescriptionRepository, PrescriptionRepository>();
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

// Application Services & FluentValidation
builder.Services.AddApplicationServices();
var aiSettings = builder.Configuration.GetSection(AiAssistantSettings.SectionName).Get<AiAssistantSettings>() ?? new AiAssistantSettings();
aiSettings.ApiKey = string.IsNullOrWhiteSpace(aiSettings.ApiKey)
    ? (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? builder.Configuration["OpenAI:ApiKey"])
    : aiSettings.ApiKey;
var configuredProvider = Environment.GetEnvironmentVariable("AI_PROVIDER") ?? builder.Configuration["AiAssistant:Provider"];
if (!string.IsNullOrWhiteSpace(configuredProvider)) aiSettings.Provider = configuredProvider;
var configuredModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? Environment.GetEnvironmentVariable("AI_MODEL");
if (!string.IsNullOrWhiteSpace(configuredModel)) aiSettings.Model = configuredModel;
var configuredEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? builder.Configuration["AiAssistant:Endpoint"];
if (!string.IsNullOrWhiteSpace(configuredEndpoint)) aiSettings.Endpoint = configuredEndpoint;

builder.Services.Configure<AiAssistantSettings>(options =>
{
    options.Provider = aiSettings.Provider;
    options.ApiKey = aiSettings.ApiKey;
    options.Model = aiSettings.Model;
    options.Endpoint = aiSettings.Endpoint;
    options.TimeoutSeconds = aiSettings.TimeoutSeconds;
});
builder.Services.Configure<OpenAiChatbotSettings>(options =>
{
    options.ApiKey = aiSettings.ApiKey;
    options.Model = aiSettings.Model;
});

// Paymob Gateway Configuration & Production Gating
var paymobSection = builder.Configuration.GetSection(PaymobSettings.SectionName);
var paymobSettings = paymobSection.Get<PaymobSettings>() ?? new PaymobSettings();
paymobSettings.ApiKey = string.IsNullOrWhiteSpace(paymobSettings.ApiKey)
    ? (Environment.GetEnvironmentVariable("PAYMOB_API_KEY") ?? builder.Configuration["Paymob:ApiKey"])
    : paymobSettings.ApiKey;
paymobSettings.HmacSecret = string.IsNullOrWhiteSpace(paymobSettings.HmacSecret)
    ? (Environment.GetEnvironmentVariable("PAYMOB_HMAC_SECRET") ?? builder.Configuration["Paymob:HmacSecret"])
    : paymobSettings.HmacSecret;
var configuredPaymobProvider = Environment.GetEnvironmentVariable("PAYMENT_PROVIDER") ?? builder.Configuration["Paymob:Provider"];
if (!string.IsNullOrWhiteSpace(configuredPaymobProvider)) paymobSettings.Provider = configuredPaymobProvider;

// Production Environment Safety Rule: Never allow Simulated payment provider in Production
if (builder.Environment.IsProduction() &&
    string.Equals(paymobSettings.Provider, "Simulated", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Production environment configuration error: Simulated payment provider cannot be enabled in Production. " +
        "Please configure legitimate Paymob Egypt gateway credentials via environment variables or secret store.");
}

builder.Services.Configure<PaymobSettings>(options =>
{
    options.Provider = paymobSettings.Provider;
    options.ApiKey = paymobSettings.ApiKey;
    options.HmacSecret = paymobSettings.HmacSecret;
    options.IntegrationId = paymobSettings.IntegrationId > 0 ? paymobSettings.IntegrationId : (int.TryParse(builder.Configuration["Paymob:IntegrationId"], out var iId) ? iId : 0);
    options.IframeId = !string.IsNullOrWhiteSpace(paymobSettings.IframeId) ? paymobSettings.IframeId : builder.Configuration["Paymob:IframeId"];
    options.BaseUrl = !string.IsNullOrWhiteSpace(paymobSettings.BaseUrl) ? paymobSettings.BaseUrl : "https://accept.paymob.com/api";
    options.Currency = !string.IsNullOrWhiteSpace(paymobSettings.Currency) ? paymobSettings.Currency : "EGP";
});

builder.Services.AddSingleton<MediCare.Web.Services.IQrCodeService, MediCare.Web.Services.QrCodeService>();

// SignalR Real-Time Communications
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

// MVC Controllers & Views
builder.Services.AddControllersWithViews();

// Health Checks for Azure App Service & Uptime Monitoring
builder.Services.AddHealthChecks();

// Rate Limiting Policies (Per-IP Protection)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Public prescription token verification: max 15 requests/minute per IP
    options.AddPolicy("PrescriptionVerificationPolicy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    // Pharmacy dispensing operations: max 10 requests/minute per IP
    options.AddPolicy("PrescriptionDispensePolicy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    // Login endpoint brute-force protection: max 5 requests/minute per IP
    options.AddPolicy("LoginRateLimitPolicy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    // Registration endpoint anti-flooding / spam protection: max 5 requests/minute per IP
    options.AddPolicy("RegistrationRateLimitPolicy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    // Public AI Chatbot endpoint rate limiter: max 10 requests/minute per IP
    options.AddPolicy("ChatbotRateLimitPolicy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

// Explicit HSTS Configuration (OWASP Standard: 1 year, include subdomains, preload)
builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});

// Configure Forwarded Headers for reverse proxy environments (e.g. IIS, Azure, Linux containers)
// Trusted proxies must be explicitly configured; if none are configured, the middleware is disabled
// to guarantee that spoofed X-Forwarded-For headers from untrusted clients cannot bypass IP rate limiters.
var knownProxiesConfig = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>();
var hasConfiguredProxies = knownProxiesConfig.Length > 0;

if (hasConfiguredProxies)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var proxy in knownProxiesConfig)
        {
            if (System.Net.IPAddress.TryParse(proxy.Trim(), out var parsedIp))
            {
                options.KnownProxies.Add(parsedIp);
            }
        }
    });
}

var app = builder.Build();

// Run DbInitializer seed data on startup
await DbInitializer.InitializeAsync(app.Services);

// Guarded one-time migration switch for legacy wwwroot attachments
if (args.Contains("--migrate-attachments"))
{
    using var scope = app.Services.CreateScope();
    var migrationHelper = scope.ServiceProvider.GetRequiredService<MediCare.Services.Common.AttachmentStorageMigrationHelper>();
    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("AttachmentMigration");

    logger.LogInformation("Starting one-time medical attachments storage migration...");
    var migrationResult = await migrationHelper.MigrateAsync(env.ContentRootPath, env.WebRootPath);
    logger.LogInformation("Attachment migration complete: Success={IsSuccess}, FilesMigrated={FilesMigrated}, FilesFailed={FilesFailed}, DatabaseRowsUpdated={DatabaseRowsUpdated}",
        migrationResult.IsSuccess, migrationResult.FilesMigrated, migrationResult.FilesFailed, migrationResult.DatabaseRowsUpdated);
    return;
}

// Emergency server-side recovery switch to unlock accounts without web UI
if (AccountRecoveryHelper.TryParseRecoveryArgument(args, out var targetEmail))
{
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("AccountRecovery");

    await AccountRecoveryHelper.UnlockUserAsync(userManager, targetEmail!, logger);
    return;
}

if (hasConfiguredProxies)
{
    app.UseForwardedHeaders();
}
app.UseSecurityHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<MustChangePasswordMiddleware>();

// Health Check Endpoint
app.MapHealthChecks("/health");

// SignalR Hub Endpoint
app.MapHub<AppointmentHub>("/hubs/appointment");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program { }

