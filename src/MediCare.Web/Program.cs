using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Repositories;
using MediCare.Data.Seed;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Contracts;
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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\mssqllocaldb;Database=MediCareDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

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

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(24);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

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
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

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

app.UseForwardedHeaders();
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

// Health Check Endpoint
app.MapHealthChecks("/health");

// SignalR Hub Endpoint
app.MapHub<AppointmentHub>("/hubs/appointment");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
