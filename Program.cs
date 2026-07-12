using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;

var builder = WebApplication.CreateBuilder(args);

// Keep logging simple and local for this student project. Windows EventLog writes can fail in sandboxed/dev environments.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Load .env file at startup
var envPath = Path.Combine(builder.Environment.ContentRootPath, ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;
        var parts = trimmed.Split('=', 2);
        if (parts.Length == 2)
        {
            var key = parts[0].Trim().Replace("export ", "");
            var val = parts[1].Trim().Trim('"').Trim('\'');
            Environment.SetEnvironmentVariable(key, val);
        }
    }

    builder.Configuration.AddEnvironmentVariables();
}

// Configure CORS
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins")
    .GetChildren()
    .Select(origin => origin.Value)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Cast<string>()
    .ToArray();

var allowedOrigins = configuredOrigins.Length > 0
    ? configuredOrigins
    : new[]
    {
        "http://localhost:5173",
        "https://localhost:5173",
        "http://127.0.0.1:5173",
        "https://127.0.0.1:5173"
    };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Configure Database Connection
ValidateProductionConfiguration(builder.Configuration, builder.Environment);
var connectionString = GetDatabaseConnectionString(builder.Configuration, builder.Environment.IsProduction());

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Keep local dev key storage inside the project data folder to avoid user-profile permission issues.
var dataProtectionPath = Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));

// Add HttpClient for AiController
builder.Services.AddHttpClient();
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("MailSettings"));
builder.Services.Configure<AdvancedEmailValidationSettings>(builder.Configuration.GetSection("AdvancedEmailValidation"));
builder.Services.PostConfigure<MailSettings>(mail =>
{
    mail.Host = FirstConfigured(mail.Host, "SMTP_HOST") ?? mail.Host;
    mail.Username = FirstConfigured(mail.Username, "SMTP_USERNAME") ?? mail.Username;
    mail.Password = FirstConfigured(mail.Password, "SMTP_PASSWORD") ?? mail.Password;
    mail.FromEmail = FirstConfigured(mail.FromEmail, "SMTP_FROM", "SMTP_USERNAME") ?? mail.FromEmail;
    mail.AppBaseUrl = FirstConfigured(null, "APP_BASE_URL", "APP_URL") ?? mail.AppBaseUrl;

    var portText = FirstConfigured(null, "SMTP_PORT");
    if (int.TryParse(portText, out var port))
    {
        mail.Port = port;
    }

    var useSslText = FirstConfigured(null, "SMTP_USE_SSL");
    if (bool.TryParse(useSslText, out var useSsl))
    {
        mail.UseSsl = useSsl;
    }

    var timeoutSecondsText = FirstConfigured(null, "SMTP_TIMEOUT_SECONDS");
    if (int.TryParse(timeoutSecondsText, out var timeoutSeconds) && timeoutSeconds is >= 1 and <= 120)
    {
        mail.TimeoutSeconds = timeoutSeconds;
    }
});
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<ImageStorageService>();
builder.Services.AddScoped<SpotifyService>();
builder.Services.AddScoped<IAdvancedEmailValidator, AdvancedEmailValidator>();
builder.Services.AddScoped<IEmailService, MailKitEmailService>();
builder.Services.AddScoped<IInviteEmailService, InviteEmailService>();

// Add MVC controllers and Razor views
builder.Services.AddControllersWithViews();

// Configure Web Host URL
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

if (app.Environment.IsProduction())
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Automatically run EF migrations at startup
using (var scope = app.Services.CreateScope())
{
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.Migrate();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogError(ex, "An error occurred while migrating or initializing the database.");
    }
}

app.UseCors("AllowFrontend");

// Serve static files (Vite build outputs to wwwroot)
app.UseDefaultFiles();
app.UseStaticFiles();

// Serve uploaded image assets from root-level data/uploads directory to survive frontend builds
var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "data", "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/data/uploads"
});

app.UseRouting();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Keep client-side routes working while serving the app through MVC.
app.MapFallbackToController("Index", "Home");

app.Run();

static string GetDatabaseConnectionString(IConfiguration configuration, bool isProduction)
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrWhiteSpace(databaseUrl))
    {
        return ConvertDatabaseUrlToNpgsqlConnectionString(databaseUrl);
    }

    var envConnectionString = Environment.GetEnvironmentVariable("DEFAULT_CONNECTION")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    if (!string.IsNullOrWhiteSpace(envConnectionString))
    {
        return envConnectionString;
    }

    var configuredConnectionString = configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(configuredConnectionString) &&
        !configuredConnectionString.Contains("YOUR_DATABASE_USER", StringComparison.OrdinalIgnoreCase) &&
        !configuredConnectionString.Contains("YOUR_DATABASE_PASSWORD", StringComparison.OrdinalIgnoreCase))
    {
        return configuredConnectionString;
    }

    if (isProduction)
    {
        throw new InvalidOperationException("Production requires DATABASE_URL, DEFAULT_CONNECTION, or ConnectionStrings:DefaultConnection with real credentials.");
    }

    return "Host=localhost;Database=aura_board;Username=postgres;Password=postgres";
}

static void ValidateProductionConfiguration(IConfiguration configuration, IHostEnvironment environment)
{
    if (!environment.IsProduction())
    {
        return;
    }

    var allowedHosts = configuration["AllowedHosts"];
    if (string.IsNullOrWhiteSpace(allowedHosts) ||
        allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(host => host is "*" or "0.0.0.0"))
    {
        throw new InvalidOperationException("Production AllowedHosts must list the deployed host names; wildcard hosts are not allowed.");
    }

    var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
    if (IsUnsafeSecret(jwtSecret))
    {
        throw new InvalidOperationException("Production JWT_SECRET must be set from the environment to a unique value of at least 32 characters.");
    }

    var configuredConnectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? Environment.GetEnvironmentVariable("DEFAULT_CONNECTION")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        ?? configuration.GetConnectionString("DefaultConnection");

    if (IsUnsafeConnectionString(configuredConnectionString))
    {
        throw new InvalidOperationException("Production database connection string is missing, placeholder, or uses unsafe local defaults.");
    }
}

static bool IsUnsafeSecret(string? secret)
{
    if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
    {
        return true;
    }

    var lowered = secret.ToLowerInvariant();
    return lowered.Contains("replace-me") ||
        lowered.Contains("replace_with") ||
        lowered.Contains("fake") ||
        lowered.Contains("placeholder") ||
        lowered.Contains("your_");
}

static bool IsUnsafeConnectionString(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return true;
    }

    var lowered = connectionString.ToLowerInvariant();
    return lowered.Contains("your_database_user") ||
        lowered.Contains("your_database_password") ||
        lowered.Contains("username=postgres;password=postgres") ||
        lowered.Contains("user id=postgres;password=postgres") ||
        lowered.Contains("://user:password@") ||
        lowered.Contains("localhost");
}

static string? FirstConfigured(string? currentValue, params string[] keys)
{
    if (!string.IsNullOrWhiteSpace(currentValue))
    {
        return currentValue;
    }

    foreach (var key in keys)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}

static string ConvertDatabaseUrlToNpgsqlConnectionString(string databaseUrl)
{
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':', 2);
    var username = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? string.Empty);
    var password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? string.Empty);
    var database = uri.AbsolutePath.TrimStart('/');

    var builder = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = database,
        Username = username,
        Password = password,
        SslMode = Npgsql.SslMode.Require
    };

    return builder.ConnectionString;
}
