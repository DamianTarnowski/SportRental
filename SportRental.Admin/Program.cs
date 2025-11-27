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
using SportRental.Admin.Services.Contracts;
using SportRental.Admin.Services.Sms;
using SportRental.Admin.Services.Storage;
using SportRental.Admin.Services.UI;
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
            "http://localhost:5014",
            "https://localhost:7083",
            "http://localhost:5015"  // dodatkowy port dla backupu
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
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HttpContextTenantProvider>();
builder.Services.AddScoped<IContractGenerator, QuestPdfContractGenerator>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("MediaStorage");
// WybĂłr SMS nadawcy: jeĹ›li jest SmsApi:Token to SmsAPI, wpp. konsola
builder.Services.AddSingleton<ISmsSender>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var token = config["SmsApi:Token"];
    if (!string.IsNullOrWhiteSpace(token))
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        return new SmsApiSender(config, factory);
    }
    return new ConsoleSmsSender();
});
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

// Background services
builder.Services.AddHostedService<SportRental.Admin.Services.Email.RentalReminderService>();
builder.Services.AddHostedService<ExpiredHoldsCleaner>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
// Pooled DbContext dla API i usĹ‚ug (scoped, ale z poolingiem)
builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));
// Pooled factory dla komponentĂłw Blazor (lokalne, niezaleĹĽne instancje na ĹĽÄ…danie)
builder.Services.AddPooledDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));
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
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Additional services from old project
builder.Services.AddScoped<SportRental.Admin.Services.Logging.IAuditLogger, SportRental.Admin.Services.Logging.DatabaseAuditLogger>();
builder.Services.AddScoped<SportRental.Admin.Services.QrCode.IQrCodeGenerator, SportRental.Admin.Services.QrCode.SimpleQrCodeGenerator>();
builder.Services.AddScoped<SportRental.Admin.Services.Sms.ISmsConfirmationService, SportRental.Admin.Services.Sms.SmsConfirmationService>();

// Authorization builder (musi byÄ‡ przed var app = builder.Build())
builder.Services.AddAuthorizationBuilder();

var mediaConfig = builder.Configuration.GetSection("MediaStorage");
var mediaAutoStart = mediaConfig.GetValue<bool?>("AutoStart") ?? false;
if (builder.Environment.IsDevelopment() && mediaAutoStart)
{
    builder.Services.AddHostedService<SportRental.Admin.Services.Media.MediaStorageProcessHostedService>();
}

var app = builder.Build();

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

app.UseHttpsRedirection();

// Swagger UI dla API
app.UseSwagger();
app.UseSwaggerUI();

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
app.MapControllers();

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
    // Ensure default tenant exists
    var tenantId = config.GetValue<Guid?>("Tenant:Id") ?? Guid.Empty;
    if (tenantId == Guid.Empty)
    {
        tenantId = Guid.NewGuid();
    }
    if (!await db.Tenants.AnyAsync(t => t.Id == tenantId))
    {
        db.Tenants.Add(new Tenant { Id = tenantId, Name = config["Tenant:Name"] ?? "Default Tenant" });
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

    // Uporządkuj użytkowników bez tenant/roli właściciela
    var unassignedUsers = userManager.Users.Where(u => u.TenantId == null || u.TenantId == Guid.Empty).ToList();
    foreach (var user in unassignedUsers)
    {
        user.TenantId = tenantId;
        await userManager.UpdateAsync(user);

        if (!await userManager.IsInRoleAsync(user, RoleNames.Owner))
        {
            await userManager.AddToRoleAsync(user, RoleNames.Owner);
        }
        if (!await userManager.IsInRoleAsync(user, RoleNames.Client))
        {
            await userManager.AddToRoleAsync(user, RoleNames.Client);
        }
    }

    // Podnieś hdtdtr@gmail.com do SuperAdmin + Owner
    var hdUser = await userManager.FindByEmailAsync("hdtdtr@gmail.com");
    if (hdUser != null)
    {
        if (hdUser.TenantId == null || hdUser.TenantId == Guid.Empty)
        {
            hdUser.TenantId = tenantId;
            await userManager.UpdateAsync(hdUser);
        }
        if (!await userManager.IsInRoleAsync(hdUser, RoleNames.SuperAdmin))
            await userManager.AddToRoleAsync(hdUser, RoleNames.SuperAdmin);
        if (!await userManager.IsInRoleAsync(hdUser, RoleNames.Owner))
            await userManager.AddToRoleAsync(hdUser, RoleNames.Owner);
        if (!await userManager.IsInRoleAsync(hdUser, RoleNames.Client))
            await userManager.AddToRoleAsync(hdUser, RoleNames.Client);
    }

    // Dodaj konto testowe właściciela, jeśli nie istnieje
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

app.Run();

public partial class Program { }
