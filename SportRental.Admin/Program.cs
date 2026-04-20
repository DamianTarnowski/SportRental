using SportRental.Admin.Components;
using SportRental.Admin.Components.Account;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
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
using SportRental.Shared.Identity;
using SportRental.Admin.Data;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

// QuestPDF license - Community is free for revenue < $1M USD
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Azure Key Vault Configuration
// Automatically uses: az login (local), Managed Identity (Azure), Visual Studio, Environment Variables
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrWhiteSpace(keyVaultUrl))
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
        logger.LogInformation("💡 For local development, secrets should be in appsettings.Development.json or user secrets");
    }
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
        policy.WithOrigins(
            "http://localhost:5002",  // WASM client dev
            "http://localhost:5014",
            "https://localhost:7083",
            "http://localhost:5015",  // dodatkowy port dla backupu
            "https://nice-tree-0359d8403.3.azurestaticapps.net",  // Azure Static Web Apps
            "https://srclient.azurewebsites.net",  // Azure App Service (stary)
            "https://srclient-blazor.azurewebsites.net"  // Azure App Service WASM client (nowy)
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
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, SportRental.Admin.Services.Tenancy.BlazorTenantProvider>();
builder.Services.AddScoped<IContractGenerator, QuestPdfContractGenerator>();
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
builder.Services.AddSingleton<ISmsSender, SmsSenderRouter>();
builder.Services.AddScoped<ISmsConfirmationService, SmsConfirmationService>();
builder.Services.AddScoped<SportRental.Admin.Services.IRentalConfirmationService, SportRental.Admin.Services.RentalConfirmationService>();

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
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddSingleton<ImageVariantService>();

builder.Services.AddSingleton(new RegistrationFeatureFlags
{
    AllowOwnerSelfRegistration = builder.Configuration.GetValue<bool?>("Features:AllowOwnerSelfRegistration") ?? true
});

// Background services
builder.Services.AddHostedService<SportRental.Admin.Services.Email.RentalReminderService>();
builder.Services.AddHostedService<ExpiredHoldsCleaner>();

// JWT Bearer for WASM / cross-origin API clients (sits alongside Identity cookies for Blazor Server).
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services.Configure<JwtOptions>(jwtSection);
var jwtSigningKey = jwtSection["SigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey) && builder.Environment.IsDevelopment())
{
    jwtSigningKey = "dev-only-signing-key-do-not-use-in-production-change-in-keyvault-aaaaaaaa";
    jwtSection["SigningKey"] = jwtSigningKey;
}
if (string.IsNullOrWhiteSpace(jwtSigningKey))
{
    throw new InvalidOperationException("Jwt:SigningKey is required in production. Configure it in Azure Key Vault as 'Jwt--SigningKey'.");
}
builder.Services.AddScoped<JwtTokenService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
// Pooled factory dla Blazor Server - tworzy instancje DbContext na żądanie
builder.Services.AddPooledDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npg => npg.MigrationsAssembly("SportRental.Infrastructure")));
// Scoped DbContext dla Identity (pobiera z factory)
builder.Services.AddScoped<ApplicationDbContext>(sp => 
    sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options => {
        options.SignIn.RequireConfirmedAccount = false; // WyĹ‚Ä…czamy wymaganie potwierdzenia konta
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<SportRental.Admin.Services.Identity.CustomUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

// Email configuration: default to NoOp (tests), enable SMTP only when explicitly configured
builder.Services.AddScoped<SportRental.Admin.Services.Email.IEmailSender>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var useSmtp = cfg.GetValue<bool?>("Email:Smtp:Enabled") ?? false;
    if (useSmtp)
    {
        var logger = sp.GetRequiredService<ILogger<SportRental.Admin.Services.Email.SmtpEmailSender>>();
        return new SportRental.Admin.Services.Email.SmtpEmailSender(cfg, logger);
    }
    else
    {
        var logger = sp.GetRequiredService<ILogger<SportRental.Admin.Services.Email.NoOpEmailSender>>();
        return new SportRental.Admin.Services.Email.NoOpEmailSender(logger);
    }
});
builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>(sp =>
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
builder.Services.AddScoped<SportRental.Admin.Services.Sms.ISmsConfirmationService, SportRental.Admin.Services.Sms.SmsConfirmationService>();

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

var app = builder.Build();

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
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

// Swagger UI tylko w dev — w produkcji ujawniałby powierzchnię API (SEC-005)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();

app.UseCors(); // Enable CORS before authentication
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

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

// REST API
app.MapSportRentalApi();
app.MapSmsApiCallbacks(); // SMSAPI delivery report callbacks
app.MapConfirmationEndpoints(); // Public rental confirmation page
app.MapControllers();
app.MapHub<SportRental.Admin.Hubs.RentalNotificationHub>("/hubs/rentals");

// Seed test data in development (from test-data.json)
if (app.Environment.IsDevelopment())
{
    using var seedScope = app.Services.CreateScope();
    var logger = seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    
    try
    {
        var seeder = new TestDataSeeder(dbFactory, seedScope.ServiceProvider.GetRequiredService<ILogger<TestDataSeeder>>());
        await seeder.SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Error during test data seeding");
    }
}

// Seed ról na starcie (SuperAdmin, Owner, Employee, Client)
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    
    await db.Database.MigrateAsync();
    
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

    // Uporządkuj użytkowników bez tenanta - ale tylko tych bez prawidłowego tenanta
    var existingTenantIds = await db.Tenants.Select(t => t.Id).ToListAsync();
    var unassignedUsers = userManager.Users
        .Where(u => u.TenantId == null || u.TenantId == Guid.Empty || !existingTenantIds.Contains(u.TenantId.Value))
        .ToList();
    
    foreach (var user in unassignedUsers)
    {
        user.TenantId = tenantId;
        await userManager.UpdateAsync(user);
        Console.WriteLine($"📝 Przypisano {user.Email} do tenanta {tenantId}");

        if (!await userManager.IsInRoleAsync(user, RoleNames.Owner))
        {
            await userManager.AddToRoleAsync(user, RoleNames.Owner);
        }
        if (!await userManager.IsInRoleAsync(user, RoleNames.Client))
        {
            await userManager.AddToRoleAsync(user, RoleNames.Client);
        }
    }

    // Podnieś hdtdtr@gmail.com do SuperAdmin + Owner + Reset hasła
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

        if (app.Environment.IsDevelopment())
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(hdUser);
            var resetResult = await userManager.ResetPasswordAsync(hdUser, resetToken, "HasloHaslo122@@@");
            if (resetResult.Succeeded)
            {
                Console.WriteLine($"🔑 [DEV] Hasło dla hdtdtr@gmail.com zresetowane do hasła deweloperskiego");
            }
        }
    }
    else if (app.Environment.IsDevelopment())
    {
        hdUser = new ApplicationUser
        {
            UserName = "hdtdtr@gmail.com",
            Email = "hdtdtr@gmail.com",
            EmailConfirmed = true,
            TenantId = tenantId
        };
        var createResult = await userManager.CreateAsync(hdUser, "HasloHaslo122@@@");
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

app.Run();

public partial class Program { }

public sealed class RegistrationFeatureFlags
{
    public bool AllowOwnerSelfRegistration { get; init; }
}
