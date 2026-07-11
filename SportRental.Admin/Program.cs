using SportRental.Admin.Components;
using SportRental.Admin.Components.Account;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using SportRental.Infrastructure.Tenancy;
using SportRental.Admin.Api;
using SportRental.Admin.Services.Auth;
using SportRental.Admin.Services.Contracts;
using SportRental.Admin.Services.Sms;
using SportRental.Admin.Services.Storage;
using SportRental.Admin.Services.UI;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.OpenApi.Models;
using SportRental.Admin.Services.Holds;
using SportRental.Admin.Services.Identity;
using SportRental.Admin.Routing;
using SportRental.Shared.Identity;
using SportRental.Admin.Data;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using System.Security.Claims;

// QuestPDF license - Community is free for revenue < $1M USD
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Azure Key Vault Configuration
// Automatically uses: az login (local), Managed Identity (Azure), Visual Studio, Environment Variables
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(keyVaultUrl))
{
    try
    {
        var secretClient = new SecretClient(new Uri(keyVaultUrl), new DefaultAzureCredential());
        builder.Configuration.AddAzureKeyVault(secretClient, new KeyVaultSecretManager());
        builder.Services.AddSingleton(_ => secretClient);
        
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
        logger.LogInformation("🔐 Azure Key Vault configured: {KeyVaultUrl}", keyVaultUrl);
    }
    catch (Exception ex)
    {
        // Key Vault not available (local development without Azure credentials)
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
        logger.LogWarning("⚠️  Azure Key Vault not available: {Message}. Using local configuration only.", ex.Message);
        logger.LogInformation("For local development, secrets should use user-secrets or environment variables");
    }
}

// Test hosts must never use real outbound providers, even when Key Vault is available.
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration["Email:Smtp:Enabled"] = "false";
    builder.Configuration["Sms:Provider"] = "Console";
    builder.Configuration["BackgroundServices:Enabled"] = "false";
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure SignalR for large file uploads
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB (default is 32KB)
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddMudServices();
builder.Services.AddControllers();

// Configure CORS for WASM Client
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // Produkcyjny Client jest bundled pod /_client (same-origin). CORS jest potrzebny
        // wyłącznie dla samodzielnego WASM uruchamianego lokalnie podczas developmentu.
        policy.WithOrigins(
            "http://localhost:5002",   // WASM client dev
            "http://localhost:5014",
            "https://localhost:7083",
            "http://localhost:5015"    // dodatkowy lokalny port
        )
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SportRental API",
        Version = "v1"
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<SportRental.Admin.Health.DbHealthCheck>("db");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // SEC-006: stricter limit for credential endpoints (login/register/guest-session)
    // 5 req/min/IP — brute force defense.
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // Odświeżenie istniejącej sesji i jawny hand-off Admin -> Client nie
    // weryfikują hasła, więc nie mogą współdzielić budżetu 5/min z endpointami
    // credentiali. Ograniczamy je osobno per zalogowany użytkownik; fallback IP
    // obowiązuje tylko wtedy, gdy uwierzytelnienie nie dostarczy identyfikatora.
    options.AddPolicy("session", context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = !string.IsNullOrWhiteSpace(userId)
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

// Persist DataProtection keys, żeby antiforgery cookies + Identity cookies przeżywały restart.
// Bez tego każdy deploy/restart unieważnia wszystkie aktywne sesje (HTTP 400 na login).
// Na Azure App Service /home/data/* jest shared między instancjami i przeżywa redeploy.
{
    var keysPath = builder.Environment.IsDevelopment()
        ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "dpkeys")
        : "/home/data/dpkeys";
    try
    {
        Directory.CreateDirectory(keysPath);
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("RentSpot.Admin");
    }
    catch (Exception ex)
    {
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
        logger.LogWarning(ex, "Nie udało się skonfigurować persistent DataProtection keys w {Path}. Używam default in-memory.", keysPath);
    }
}

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, SportRental.Admin.Services.Tenancy.BlazorTenantProvider>();
builder.Services.AddScoped<IContractGenerator, QuestPdfContractGenerator>();
builder.Services.AddSingleton<IContractAccessLinkService, ContractAccessLinkService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("MediaStorage");

// SerwerSMS.pl Configuration - integracja z SerwerSMS.pl
// Dokumentacja: https://dev.serwersms.pl/https-api-v2/wprowadzenie
// Panel: Ustawienia interfejsów → HTTP API → Użytkownicy API
builder.Services.Configure<SmsRoutingSettings>(builder.Configuration.GetSection(SmsRoutingSettings.SectionName));
builder.Services.Configure<SerwerSmsSettings>(builder.Configuration.GetSection(SerwerSmsSettings.SectionName));
builder.Services.Configure<SmsApiSettings>(builder.Configuration.GetSection(SmsApiSettings.SectionName));
builder.Services.AddHttpClient("SerwerSms");
builder.Services.AddSingleton<SmsApiSender>();
builder.Services.AddSingleton<SerwerSmsSender>();
builder.Services.AddSingleton<ConsoleSmsSender>();
builder.Services.AddSingleton<SmsSenderRouter>();
// Decorator: w demo loguje "DEMO SANDBOX" zamiast słać realny SMS. Scoped bo IDemoGuard jest scoped.
builder.Services.AddScoped<ISmsSender>(sp => new SportRental.Admin.Services.Demo.DemoAwareSmsSender(
    sp.GetRequiredService<SmsSenderRouter>(),
    sp.GetRequiredService<SportRental.Admin.Services.Demo.IDemoGuard>(),
    sp.GetRequiredService<ILogger<SportRental.Admin.Services.Demo.DemoAwareSmsSender>>()));
if (builder.Configuration.GetValue<bool>(SmsRoutingSettings.LegacyReplyConfirmationEnabledKey))
{
#pragma warning disable CS0618 // Legacy flow is registered only when explicitly enabled.
    builder.Services.AddScoped<ISmsConfirmationService, SmsConfirmationService>();
#pragma warning restore CS0618
}
builder.Services.AddScoped<SportRental.Admin.Services.IRentalConfirmationService, SportRental.Admin.Services.RentalConfirmationService>();
builder.Services.AddSingleton<SportRental.Admin.Services.IReviewSurveyTokenService, SportRental.Admin.Services.ReviewSurveyTokenService>();
builder.Services.AddScoped<SportRental.Admin.Services.ICustomerTrustCalculator, SportRental.Admin.Services.CustomerTrustCalculator>();

// === Floating chat (asystent AI) ===
// Sekrety idą przez Key Vault (KeyVault:Url w appsettings.json). Lokalnie fallback przez
// dotnet user-secrets — appsettings.Development.json/repo NIE zawiera sekretów (repo public).
// Zapisz lokalnie:
//   dotnet user-secrets set "OpenAI:Endpoint" "https://foundrypolska.cognitiveservices.azure.com/"
//   dotnet user-secrets set "OpenAI:ApiKey" "..."
//   dotnet user-secrets set "OpenAI:TextDeployment" "gpt-5.5"
// Integracja jest opcjonalna: brak konfiguracji wyłącza elementy AI, ale nie może
// przerwać obwodu Blazor na stronach niezwiązanych z asystentem.
builder.Services.AddSingleton<SportRental.Admin.Services.Ai.AzureOpenAiClientProvider>();
builder.Services.AddScoped<SportRental.Admin.Services.Chat.FloatingChatService>();
builder.Services.AddScoped<SportRental.Admin.Services.Chat.FeedbackService>();
builder.Services.AddScoped<SportRental.Admin.Services.Chat.ReadToolService>();
builder.Services.AddScoped<SportRental.Admin.Services.Chat.WriteToolService>();
builder.Services.AddScoped<SportRental.Admin.Services.Chat.ChatPersistenceService>();
builder.Services.AddScoped<SportRental.Admin.Services.Chat.ChatSettingsService>();
builder.Services.AddScoped<SportRental.Admin.Payments.CheckoutFinalizationService>();
builder.Services.AddScoped<SportRental.Admin.Services.IBusinessHoursService, SportRental.Admin.Services.BusinessHoursService>();
builder.Services.AddScoped<SportRental.Admin.Services.IPriceCalculator, SportRental.Admin.Services.PriceCalculator>();
builder.Services.AddScoped<SportRental.Admin.Services.IInvoiceService, SportRental.Admin.Services.InvoiceService>();
builder.Services.AddScoped<SportRental.Admin.Services.IDiscountService, SportRental.Admin.Services.DiscountService>();
builder.Services.AddScoped<SportRental.Admin.Services.IVoucherService, SportRental.Admin.Services.VoucherService>();
builder.Services.AddScoped<SportRental.Admin.Services.IGoogleCalendarService, SportRental.Admin.Services.GoogleCalendarService>();
builder.Services.AddScoped<SportRental.Admin.Data.DemoTenantSeeder>();
builder.Services.AddScoped<SportRental.Admin.Services.Dashboard.DashboardMetricsService>();
builder.Services.AddScoped<SportRental.Admin.Services.Search.SearchService>();
builder.Services.AddScoped<SportRental.Admin.Services.Ai.IProductAiAssistant, SportRental.Admin.Services.Ai.ProductAiAssistant>();
builder.Services.AddScoped<SportRental.Admin.Services.Demo.IDemoGuard, SportRental.Admin.Services.Demo.DemoGuard>();
builder.Services.AddScoped<SportRental.Admin.Services.Chat.OpenAiChatService>();
builder.Services.AddScoped<SportRental.Admin.Services.Chat.ChatToolHandler>();

// SignalR Hub for real-time rental notifications
builder.Services.AddSingleton<SportRental.Admin.Hubs.IRentalNotificationService, SportRental.Admin.Hubs.RentalNotificationService>();

// WewnÄ™trzny blob: domyĹ›lnie App_Data (+ mapowanie StaticFiles), alternatywnie wwwroot
builder.Services.AddSingleton<IFileStorage>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var provider = cfg["Storage:Provider"]?.ToLowerInvariant();

    logger.LogInformation("Storage Provider: {Provider}", provider ?? "auto-detect");

    return provider switch
    {
        // Azure Blob Storage (Production)
        "azureblob" or "blob" => new SportRental.Admin.Services.Storage.AzureBlobStorage(cfg, 
            sp.GetRequiredService<ILogger<SportRental.Admin.Services.Storage.AzureBlobStorage>>()),

        // Remote MediaStorage microservice
        "remote" or "mediastorage" => CreateRemoteFileStorage(cfg, sp),

        // Local App_Data (Development)
        "appdata" => new SportRental.Admin.Services.Storage.AppDataFileStorage(cfg),

        // Local wwwroot
        "local" => new LocalFileStorage(sp.GetRequiredService<IWebHostEnvironment>()),

        // S3-compatible
        "s3" => new SportRental.Admin.Services.Storage.S3FileStorage(cfg),

        // Auto-detect
        _ => AutoDetectStorageProvider(cfg, sp, logger)
    };
});

static IFileStorage AutoDetectStorageProvider(IConfiguration cfg, IServiceProvider sp, ILogger logger)
{
    // Check Azure Blob first
    var azureBlobConn = cfg["Storage:AzureBlob:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(azureBlobConn))
    {
        logger.LogInformation("Auto-detected: Azure Blob Storage");
        return new SportRental.Admin.Services.Storage.AzureBlobStorage(cfg, 
            sp.GetRequiredService<ILogger<SportRental.Admin.Services.Storage.AzureBlobStorage>>());
    }

    // Check Remote MediaStorage
    var mediaBaseUrl = cfg["MediaStorage:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(mediaBaseUrl))
    {
        logger.LogInformation("Auto-detected: Remote MediaStorage");
        return CreateRemoteFileStorage(cfg, sp);
    }

    // Default: App_Data
    logger.LogInformation("Auto-detected: App_Data Storage (development)");
    var useAppData = cfg.GetValue<bool?>("Storage:UseAppData") ?? true;
    if (useAppData)
        return new SportRental.Admin.Services.Storage.AppDataFileStorage(cfg);
    return new LocalFileStorage(sp.GetRequiredService<IWebHostEnvironment>());
}

static RemoteFileStorage CreateRemoteFileStorage(IConfiguration cfg, IServiceProvider sp)
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var client = factory.CreateClient("MediaStorage");
    var baseUrl = cfg["MediaStorage:BaseUrl"];
    if (client.BaseAddress is null && !string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl);
    }
    return new RemoteFileStorage(client, cfg);
}
// NXRE QA #4: per-device theme — Scoped zamiast Singleton (Singleton = wszyscy userzy współdzielili stan!)
builder.Services.AddScoped<ThemeService>();
builder.Services.AddSingleton<ImageVariantService>();

builder.Services.AddSingleton(new RegistrationFeatureFlags
{
    AllowOwnerSelfRegistration = builder.Configuration.GetValue<bool?>("Features:AllowOwnerSelfRegistration") ?? true
});

// Background jobs can be disabled for local smoke tests against a shared database.
// Production behavior stays enabled by default.
if (builder.Configuration.GetValue("BackgroundServices:Enabled", true))
{
    builder.Services.AddHostedService<SportRental.Admin.Services.Demo.DemoTenantCleanupService>();
    builder.Services.AddHostedService<SportRental.Admin.Services.Email.RentalReminderService>();
    builder.Services.AddHostedService<SportRental.Admin.Services.Email.ReviewRequestService>();
    builder.Services.AddHostedService<ExpiredHoldsCleaner>();
}

// JWT Bearer for WASM / cross-origin API clients (sits alongside Identity cookies for Blazor Server).
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services.Configure<JwtOptions>(jwtSection);
var jwtSigningKey = jwtSection["SigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey) &&
    (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")))
{
    // Testing też dostaje fake key — WebApplicationFactory<Program> w testach integracyjnych
    // bootstrapuje Program.cs i bez tego pada InvalidOperationException przed CreateClient.
    jwtSigningKey = "dev-only-signing-key-do-not-use-in-production-change-in-keyvault-aaaaaaaa";
    jwtSection["SigningKey"] = jwtSigningKey;
}
if (string.IsNullOrWhiteSpace(jwtSigningKey))
{
    throw new InvalidOperationException("Jwt:SigningKey is required in production. Configure it in Azure Key Vault as 'Jwt--SigningKey'.");
}
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<CustomerIdentityService>();
builder.Services.AddScoped<ExternalLoginProvisioningService>();
builder.Services.AddScoped<AccidentalOwnerPromotionRepair>();

var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    });

// Google OAuth — sekrety z Key Vault (GoogleOAuth--ClientId / GoogleOAuth--ClientSecret),
// user-secrets albo zmiennych środowiskowych w dev. Fallback: stara nazwa Google:*.
// Jeśli brak — przycisk "Zaloguj przez Google" się nie pokaże
// (ExternalLoginPicker filtruje providery zarejestrowane w SignInManager).
var googleClientId = builder.Configuration["GoogleOAuth:ClientId"]
                     ?? builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["GoogleOAuth:ClientSecret"]
                         ?? builder.Configuration["Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        // Default callback path = /signin-google. Pamiętaj wpisać w Authorized redirect URIs:
        //   https://app.example.com/signin-google
        //   https://app.rentspot.eu/signin-google (po podpięciu custom domain)
        //   http://localhost:<port>/signin-google (dev)
        options.SaveTokens = true;
    });
}

authBuilder.AddIdentityCookies();

builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"] ?? "SportRental",
            ValidAudience = jwtSection["Audience"] ?? "SportRental.Client",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // SEC-009: WASM trzyma JWT w HttpOnly cookie zamiast w localStorage.
        // Jeśli nie ma nagłówka Authorization, weź token z cookie.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token) &&
                    ctx.Request.Cookies.TryGetValue(SportRental.Admin.Api.Endpoints.AccessTokenCookieName, out var cookieToken) &&
                    !string.IsNullOrWhiteSpace(cookieToken))
                {
                    ctx.Token = cookieToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// W testach (WebApplicationFactory<Program> bez Configuration provider) DefaultConnection jest
// pusty — ApiTests overriduje DbContextOptions na InMemory później. Daj fake-string żeby
// AddDbContextFactory<>().UseNpgsql(...) nie crashował na DI registration; runtime test będzie
// używał zoverridowanego provider'a InMemory i nigdy nie zadzwoni do tego connection.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? (builder.Environment.IsEnvironment("Testing")
        ? "Host=localhost;Database=__test_placeholder__;Username=na;Password=na"
        : throw new InvalidOperationException("Connection string 'DefaultConnection' not found."));
// Zwykła fabryka (bez poolingu): ApplicationDbContext przechowuje mutable tenant context.
// Pooling mógłby przenieść `_tenantId` pomiędzy żądaniami, bo własne pola kontekstu nie
// są częścią automatycznego resetu stanu EF.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npg => npg.MigrationsAssembly("SportRental.Infrastructure")));
// Scoped DbContext dla Identity (pobiera z factory)
builder.Services.AddScoped<ApplicationDbContext>(sp => 
    sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options => {
        // Publiczne konto Client nie może otrzymać sesji dla niezweryfikowanego adresu.
        // Konta pracownicze tworzone z zaproszeń są potwierdzane przy provisioningu.
        options.SignIn.RequireConfirmedAccount = true;
        // SEC-002: policy zgodna z ASVS L2 — min 12 znaków + 3 z 4 klas znaków.
        // Wcześniej było 6 znaków bez wymagań (DoS na słownik).
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false; // zostawiamy opcjonalne (3 z 4 klas wystarczy)
        options.Password.RequiredUniqueChars = 4;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<SportRental.Admin.Services.Identity.CustomUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

// Email configuration: default to NoOp (tests), enable SMTP only when explicitly configured.
// W demo: DemoAwareEmailSender wrap loguje zamiast wysyłać.
builder.Services.AddScoped<SportRental.Admin.Services.Email.IEmailSender>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var useSmtp = cfg.GetValue<bool?>("Email:Smtp:Enabled") ?? false;
    SportRental.Admin.Services.Email.IEmailSender inner = useSmtp
        ? new SportRental.Admin.Services.Email.SmtpEmailSender(cfg, sp.GetRequiredService<ILogger<SportRental.Admin.Services.Email.SmtpEmailSender>>())
        : new SportRental.Admin.Services.Email.NoOpEmailSender(sp.GetRequiredService<ILogger<SportRental.Admin.Services.Email.NoOpEmailSender>>());

    return new SportRental.Admin.Services.Demo.DemoAwareEmailSender(
        inner,
        sp.GetRequiredService<SportRental.Admin.Services.Demo.IDemoGuard>(),
        sp.GetRequiredService<ILogger<SportRental.Admin.Services.Demo.DemoAwareEmailSender>>());
});
// Adapter Identity musi mieć ten sam (scoped) lifetime co tenant-aware sender.
// Singleton rozwiązywał scoped DemoAwareEmailSender z root providera i powodował
// HTTP 500 przy publicznej rejestracji klienta, gdy walidacja scope'ów była włączona.
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>(sp =>
    sp.GetRequiredService<SportRental.Admin.Services.Email.IEmailSender>());
builder.Services.AddScoped<IEmailSender<ApplicationUser>>(sp =>
{
    var emailSender = sp.GetRequiredService<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>();
    return new IdentityNoOpEmailSender(emailSender);
});

// Additional services from old project
builder.Services.AddScoped<SportRental.Admin.Services.Logging.IAuditLogger, SportRental.Admin.Services.Logging.DatabaseAuditLogger>();
builder.Services.AddScoped<SportRental.Admin.Services.QrCode.IQrCodeGenerator, SportRental.Admin.Services.QrCode.SimpleQrCodeGenerator>();
builder.Services.AddScoped<SportRental.Admin.Services.QrCode.IBarcodeGenerator, SportRental.Admin.Services.QrCode.BarcodeGenerator>();
builder.Services.AddScoped<SportRental.Admin.Services.IQrLabelGenerator, SportRental.Admin.Services.QrLabelGenerator>();

// Stripe Payment Gateway
builder.Services.Configure<SportRental.Admin.Payments.StripeOptions>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddSingleton<SportRental.Admin.Payments.IPaymentGateway, SportRental.Admin.Payments.StripePaymentGateway>();

// Authorization builder (musi być przed var app = builder.Build())
builder.Services.AddAuthorizationBuilder();

var mediaConfig = builder.Configuration.GetSection("MediaStorage");
var mediaAutoStart = mediaConfig.GetValue<bool?>("AutoStart") ?? false;
if (builder.Environment.IsDevelopment() && mediaAutoStart)
{
    builder.Services.AddHostedService<SportRental.Admin.Services.Media.MediaStorageProcessHostedService>();
}

// Perf: in-memory cache (tenant lookups, business hours, settings)
builder.Services.AddMemoryCache();
builder.Services.AddScoped<SportRental.Admin.Services.Cache.IAppCache, SportRental.Admin.Services.Cache.AppCache>();

// Perf: response compression (Brotli/Gzip dla HTML/JSON/CSS) — duża oszczędność transferu
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    o.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
        .Concat(new[] { "application/json", "text/html", "text/css", "application/javascript", "image/svg+xml" });
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(o =>
{
    o.Level = System.IO.Compression.CompressionLevel.Fastest;
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(o =>
{
    o.Level = System.IO.Compression.CompressionLevel.Fastest;
});

var app = builder.Build();

// Włącz response compression przed innymi middleware
app.UseResponseCompression();

// Test SMS: dotnet run --project SportRental.Admin -- --test-sms 667362375
var testSmsArg = args.FirstOrDefault(a => a.StartsWith("--test-sms", StringComparison.OrdinalIgnoreCase));
if (testSmsArg != null)
{
    var phone = args.SkipWhile(a => !a.StartsWith("--test-sms")).Skip(1).FirstOrDefault() 
                ?? testSmsArg.Split('=', 2).ElementAtOrDefault(1) 
                ?? "667362375";
    await SportRental.Admin.Tests.SmsIntegrationTest.RunAsync(phone);
    return;
}

// Jednorazowe seeding danych demo: dotnet run --project SportRental.Admin -- --seed-demo [--seed-email=hdtdtr@gmail.com]
if (args.Any(a => a.Equals("--seed-demo", StringComparison.OrdinalIgnoreCase)))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<DemoDataSeeder>>();
    var seeder = new DemoDataSeeder(
        scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
        logger);

    var emailArg = args.FirstOrDefault(a => a.StartsWith("--seed-email=", StringComparison.OrdinalIgnoreCase));
    var seedEmail = emailArg?.Split('=', 2)[1] ?? "hdtdtr@gmail.com";
    await seeder.SeedAsync(seedEmail);
    logger.LogInformation("Demo seeding finished. Exiting.");
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Ujednolicone odpowiedzi ProblemDetails takĹĽe w dev
app.UseExceptionHandler();

// HTTPS redirect tylko w produkcji - w dev klient WASM łączy się po HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// SEC-004: security headers (defense-in-depth). CSP celowo pominięte na tym etapie —
// wymaga osobnej konfiguracji pod Blazor Server + MudBlazor + Stripe.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // Kamera DOZWOLONA dla same-origin — skaner kodów kreskowych w /admin/rentals
    // używa getUserMedia() przez bibliotekę html5-qrcode. Wcześniejsze `camera=()`
    // blokowało skaner na wszystkich urządzeniach (bug zgłoszony przez inwestora).
    // Latarka (torch) jest konfigurowana przez `camera` permission — nie ma osobnego tokena.
    headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
    await next();
});

// Swagger UI tylko w dev — w produkcji ujawniałby powierzchnię API (SEC-005)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(); // Enable CORS before authentication
app.UseAuthentication();
// Rate limiter jest po Authentication, aby polityka `session` mogła partycjonować
// żądania po NameIdentifier zamiast wrzucać wszystkich użytkowników do jednego IP.
app.UseRateLimiter();
app.UseAuthorization();
app.UseAntiforgery();

// Client WASM pod /_client/* — izolowany branch pipeline który OMIJA endpoint routing.
// Powód: MapStaticAssets() rejestruje endpointy z manifestu Admin które mają nieaktualne
// wpisy dla /_client/* (hashowane WASM Client nie są w manifeście Admin, niektóre
// AssetFile-entries wyrzucają 500 mimo że plik fizycznie jest). MapWhen tworzy branch
// pipeline — request na /_client/* nigdy nie dochodzi do global endpoint routing.
var clientWebRoot = Path.Combine(app.Environment.WebRootPath, "_client");
if (Directory.Exists(clientWebRoot))
{
    var clientFileProvider = new PhysicalFileProvider(clientWebRoot);
    // Używamy Map() (nie MapWhen) bo Map ZMIENIA PathBase — zjada prefix /_client z Path,
    // dzięki czemu UseStaticFiles bez RequestPath bezpośrednio szuka pliku w FileProvider
    // (wwwroot/_client). MapWhen zostawiał /_client w Path, więc UseStaticFiles z
    // RequestPath="/_client" nie trafiał, a wszystko trafiało do SPA fallback.
    app.Map("/_client", branch =>
    {
        branch.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = clientFileProvider,
            ServeUnknownFileTypes = true
        });
        // SPA fallback dla Blazor routing (/_client/reviews, /_client/my-rentals, itp.)
        branch.Run(async ctx =>
        {
            ctx.Response.ContentType = "text/html";
            ctx.Response.Headers.CacheControl = "no-cache";
            await ctx.Response.SendFileAsync(clientFileProvider.GetFileInfo("index.html"));
        });
    });
}

app.MapStaticAssets();

// Serwowanie plikĂłw z App_Data (wewnÄ™trzny blob) pod /files
var filesRequestPath = builder.Configuration["Storage:RequestPath"] ?? "/files";
var filesRoot = builder.Configuration["Storage:RootPath"] ?? Path.Combine(AppContext.BaseDirectory, "App_Data");
Directory.CreateDirectory(filesRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(filesRoot),
    RequestPath = filesRequestPath
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Health endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// User-friendly redirects — `/login` istnieje tylko w Client WASM pod
// `/_client/login`, a Admin używa `/Account/Login`.
app.MapGet("/login", () => Results.Redirect("/Account/Login", permanent: false))
   .AllowAnonymous();
app.MapGet("/register", () => Results.Redirect("/Account/Register", permanent: false))
   .AllowAnonymous();
app.MapGet("/logout", () => Results.Redirect("/Account/Logout", permanent: false))
   .AllowAnonymous();

// Starszy WASM mógł pozostać otwarty podczas deployu i nadal emitować ścieżki
// root-relative (np. /products). Nie pozwalamy takiej karcie wypaść z klienta.
app.MapLegacyClientRouteRedirects();

// REST API
app.MapSportRentalApi();
app.MapSmsApiCallbacks(); // SMSAPI delivery report callbacks
app.MapConfirmationEndpoints(); // Public rental confirmation page
app.MapChatEndpoints(); // Floating chat asystent — /api/chat/send, /api/chat/feedback
app.MapRealtimeEndpoints(); // Voice (Azure Realtime API) — /api/realtime/session, /function/{name}
app.MapControllers();
app.MapHub<SportRental.Admin.Hubs.RentalNotificationHub>("/hubs/rentals");

// (/_client/* obsługiwany przez MapWhen branch wcześniej — SPA fallback + static files
// załatwione tam, endpoint routing dla _client nie potrzebny.)

// Seed ról na starcie (SuperAdmin, Owner, Employee, Client) — pomijamy w środowisku testowym,
// bo WebApplicationFactory<Program> recyklingowanego pomiędzy testami zamyka stdout writer
// i Console.WriteLine z tej sekcji rzuca ObjectDisposedException w drugim cyklu.
if (!app.Environment.IsEnvironment("Testing"))
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    
    // Migrate tylko gdy provider jest relacyjny — w testach DbContext jest InMemory
    // i MigrateAsync() rzuca InvalidOperationException.
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
    
    // Ensure default tenant exists - use existing tenant with products if available
    var tenantId = config.GetValue<Guid?>("Tenant:Id") ?? Guid.Empty;
    
    // Jeśli nie ma w konfiguracji, spróbuj użyć istniejącego tenanta z produktami
    if (tenantId == Guid.Empty)
    {
        // Znajdź pierwszy tenant który ma produkty
        var existingTenantWithProducts = await db.Tenants
            .Where(t => db.Products.Any(p => p.TenantId == t.Id))
            .OrderByDescending(t => db.Products.Count(p => p.TenantId == t.Id))
            .FirstOrDefaultAsync();
        
        if (existingTenantWithProducts != null)
        {
            tenantId = existingTenantWithProducts.Id;
            Console.WriteLine($"✅ Użyto istniejącego tenanta: {existingTenantWithProducts.Name} ({tenantId})");
        }
        else
        {
            // Tylko jeśli nie ma żadnego tenanta z produktami, utwórz nowy
            tenantId = Guid.NewGuid();
            db.Tenants.Add(new Tenant { Id = tenantId, Name = config["Tenant:Name"] ?? "Default Tenant" });
            await db.SaveChangesAsync();
            Console.WriteLine($"✅ Utworzono nowy tenant: {tenantId}");
        }
    }
    else if (!await db.Tenants.AnyAsync(t => t.Id == tenantId))
    {
        db.Tenants.Add(new Tenant { Id = tenantId, Name = config["Tenant:Name"] ?? "Default Tenant" });
        await db.SaveChangesAsync();
        Console.WriteLine($"✅ Utworzono tenant z konfiguracji: {tenantId}");
    }
    
    // Upewnij się że CompanyInfo istnieje dla domyślnego tenanta
    if (!await db.CompanyInfos.AnyAsync(ci => ci.TenantId == tenantId))
    {
        db.CompanyInfos.Add(new CompanyInfo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = config["Tenant:Name"] ?? "Default Tenant",
            Address = "ul. Sportowa 1, 00-001 Warszawa",
            PhoneNumber = "+48 123 456 789",
            Email = "kontakt@sportrental.pl",
            NIP = "1234567890",
            REGON = "123456789",
            LegalForm = "Jednoosobowa działalność gospodarcza",
            OpeningHours = "Pon-Pt 9:00-18:00, Sob 10:00-14:00",
            Description = "Profesjonalna wypożyczalnia sprzętu sportowego",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
    string[] roles = [RoleNames.SuperAdmin, RoleNames.Owner, RoleNames.Employee, RoleNames.Client];
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid> { Name = role, NormalizedName = role.ToUpperInvariant() });
        }
    }

    var adminEmail = config["Admin:Email"];
    var adminPassword = config["Admin:Password"];
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin == null)
        {
            admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                TenantId = tenantId
            };
            var createResult = await userManager.CreateAsync(admin, adminPassword);
            if (createResult.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, RoleNames.SuperAdmin);
                await userManager.AddToRoleAsync(admin, RoleNames.Client);
            }
        }
        else
        {
            if (admin.TenantId == null)
            {
                admin.TenantId = tenantId;
                await userManager.UpdateAsync(admin);
            }
            if (!await userManager.IsInRoleAsync(admin, RoleNames.SuperAdmin))
            {
                await userManager.AddToRoleAsync(admin, RoleNames.SuperAdmin);
            }
            if (!await userManager.IsInRoleAsync(admin, RoleNames.Client))
            {
                await userManager.AddToRoleAsync(admin, RoleNames.Client);
            }
        }
    }

    // Globalni klienci marketplace celowo nie mają TenantId. Starszy kod startowy
    // przypisywał takie konta do domyślnego tenanta i nadawał Owner. Naprawiamy
    // wyłącznie konta rozpoznane po globalnym profilu Customer i braku członkostwa
    // TenantUser Owner; nigdy nie nadajemy ról na podstawie braku TenantId.
    var existingTenantIds = await db.Tenants.Select(t => t.Id).ToListAsync();
    var accidentalOwnerRepair = scope.ServiceProvider
        .GetRequiredService<AccidentalOwnerPromotionRepair>();
    await accidentalOwnerRepair.RepairAsync();

    // Podnieś hdtdtr@gmail.com do SuperAdmin + Owner + Reset hasła.
    // Hasło deweloperskie czytane z konfiguracji (user-secrets / env Admin__DevPassword) —
    // NIE trzymamy sekretu w kodzie. Bez ustawienia tej wartości seed/reset hasła jest pomijany.
    var devSeedPassword = config["Admin:DevPassword"];
    var hdUser = await userManager.FindByEmailAsync("hdtdtr@gmail.com");
    if (hdUser != null)
    {
        // Przypisz do tenanta tylko jeśli nie ma prawidłowego
        var hasValidTenant = hdUser.TenantId.HasValue && existingTenantIds.Contains(hdUser.TenantId.Value);
        if (!hasValidTenant)
        {
            hdUser.TenantId = tenantId;
            await userManager.UpdateAsync(hdUser);
            Console.WriteLine($"📝 hdtdtr@gmail.com przypisany do tenanta {tenantId}");
        }
        else
        {
            Console.WriteLine($"✅ hdtdtr@gmail.com już ma prawidłowy tenant: {hdUser.TenantId}");
        }
        if (!await userManager.IsInRoleAsync(hdUser, RoleNames.SuperAdmin))
            await userManager.AddToRoleAsync(hdUser, RoleNames.SuperAdmin);
        if (!await userManager.IsInRoleAsync(hdUser, RoleNames.Owner))
            await userManager.AddToRoleAsync(hdUser, RoleNames.Owner);
        if (!await userManager.IsInRoleAsync(hdUser, RoleNames.Client))
            await userManager.AddToRoleAsync(hdUser, RoleNames.Client);

        if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(devSeedPassword))
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(hdUser);
            var resetResult = await userManager.ResetPasswordAsync(hdUser, resetToken, devSeedPassword);
            if (resetResult.Succeeded)
            {
                Console.WriteLine($"🔑 [DEV] Hasło dla hdtdtr@gmail.com zresetowane (Admin:DevPassword)");
            }
        }
    }
    else if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(devSeedPassword))
    {
        hdUser = new ApplicationUser
        {
            UserName = "hdtdtr@gmail.com",
            Email = "hdtdtr@gmail.com",
            EmailConfirmed = true,
            TenantId = tenantId
        };
        var createResult = await userManager.CreateAsync(hdUser, devSeedPassword);
        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(hdUser, RoleNames.SuperAdmin);
            await userManager.AddToRoleAsync(hdUser, RoleNames.Owner);
            await userManager.AddToRoleAsync(hdUser, RoleNames.Client);
            Console.WriteLine($"✨ [DEV] Utworzono konto hdtdtr@gmail.com z hasłem deweloperskim");
        }
    }

    if (app.Environment.IsDevelopment())
    {
        var testOwnerEmail = "owner@test.local";
        var testOwnerPass = "Owner123!";
        var testOwner = await userManager.FindByEmailAsync(testOwnerEmail);
        if (testOwner == null)
        {
            testOwner = new ApplicationUser
            {
                UserName = testOwnerEmail,
                Email = testOwnerEmail,
                EmailConfirmed = true,
                TenantId = tenantId
            };
            var createResult = await userManager.CreateAsync(testOwner, testOwnerPass);
            if (createResult.Succeeded)
            {
                await userManager.AddToRoleAsync(testOwner, RoleNames.Owner);
                await userManager.AddToRoleAsync(testOwner, RoleNames.Client);
            }
        }
    }
}

// Seed danych developerskich dopiero po migracjach i utworzeniu domyślnego tenanta.
if (app.Environment.IsDevelopment())
{
    using var seedScope = app.Services.CreateScope();
    var logger = seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

    try
    {
        var seeder = new TestDataSeeder(
            dbFactory,
            seedScope.ServiceProvider.GetRequiredService<ILogger<TestDataSeeder>>());
        await seeder.SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Error during test data seeding");
    }
}

app.Run();

public partial class Program { }

public sealed class RegistrationFeatureFlags
{
    public bool AllowOwnerSelfRegistration { get; init; }
}
