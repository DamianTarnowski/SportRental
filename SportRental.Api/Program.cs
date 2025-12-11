using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;
using SportRental.Api.Payments;
using SportRental.Api.Auth;
using SportRental.Api.Tenants;
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

// DbContext (PostgreSQL) - używamy DbContextPool dla lepszej performance!
// Note: RefreshToken entity is configured via DbContext.Set<RefreshToken>() for API-specific auth
// Read connection string once at startup to avoid scoped service issues
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required");

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString)
);

// Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false; // W produkcji: true
    
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSettings["SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey is required");

builder.Services.Configure<JwtOptions>(jwtSettings);
builder.Services.AddScoped<JwtTokenService>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// Stripe Payment Gateway
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddSingleton<IPaymentGateway, StripePaymentGateway>();

// Keep Mock for tests
builder.Services.AddSingleton<MockPaymentGateway>();
builder.Services.AddSingleton<ICheckoutSessionService, StripeCheckoutSessionService>();

// Email Services
builder.Services.AddScoped<SportRental.Api.Services.Email.IEmailSender>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<SportRental.Api.Services.Email.SmtpEmailSender>>();
    return new SportRental.Api.Services.Email.SmtpEmailSender(config, logger);
});
builder.Services.AddScoped<SportRental.Api.Services.Email.RentalConfirmationEmailService>();

// PDF Contract Services
builder.Services.AddScoped<SportRental.Api.Services.Contracts.IPdfContractService, SportRental.Api.Services.Contracts.PdfContractService>();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SportRental API",
        Version = "v1",
        Description = "Minimal API dla systemu wypożyczalni sportowej z autoryzacją JWT"
    });

    // XML comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);

    // Security: JWT Bearer
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Wprowadź JWT token otrzymany z /api/auth/login"
    });

    // Security: X-Tenant-Id header (dla backward compatibility z istniejącymi endpoints)
    c.AddSecurityDefinition("X-Tenant-Id", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "X-Tenant-Id",
        Type = SecuritySchemeType.ApiKey,
        Description = "Guid identyfikujący tenanta (opcjonalny - JWT zawiera tenant-id w claims)"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }, new List<string>()
        }
    });
});

// CORS (dla lokalnego klienta Blazor/WASM i produkcji)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var isDevelopment = builder.Environment.IsDevelopment();
        
        if (isDevelopment)
        {
            // Development: Allow localhost
            policy.WithOrigins(
                "http://localhost:5002", 
                "https://localhost:5002", 
                "http://localhost:5173", 
                "https://localhost:7083", 
                "http://localhost:5014",
                "http://localhost:5004")
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            // Production: Allow same-origin (nginx proxy) and specific domains if needed
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

Guid GetTenantId(HttpRequest request)
{
    var user = request.HttpContext.User;
    if (user?.Identity?.IsAuthenticated == true)
    {
        var claim = user.FindFirst("tenant-id");
        if (claim != null && Guid.TryParse(claim.Value, out var tenantClaimId))
        {
            return tenantClaimId;
        }
    }

    if (request.Headers.TryGetValue("X-Tenant-Id", out var values) && Guid.TryParse(values.FirstOrDefault(), out var tenantId))
        return tenantId;

    return Guid.Empty;
}

// Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.DocumentTitle = "SportRental API Docs";
        o.DisplayRequestDuration();
        o.EnableDeepLinking();
        o.DefaultModelsExpandDepth(1);
        o.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    });
}

app.UseCors();
app.UseHttpsRedirection();

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();


app.MapGet("/", () => "SportRental API").WithName("Root").WithTags("System");

// Products
app.MapGet("/api/products", async (HttpRequest http, ApplicationDbContext db, CancellationToken ct) =>
{
    var tenantId = GetTenantId(http);
    
    // Jeśli brak tenant-id (Guid.Empty), zwróć produkty ze wszystkich wypożyczalni
    var query = db.Products.AsQueryable();
    if (tenantId != Guid.Empty)
    {
        query = query.Where(p => p.TenantId == tenantId);
    }
    
    var products = await query
        .Select(p => new ProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Sku = p.Sku,
            Category = p.Category,
            ImageUrl = p.ImageUrl,
            ImageBasePath = p.ImageBasePath,
            DailyPrice = p.DailyPrice,
            Description = p.Description,
            FullImageUrl = p.ImageUrl,
            IsAvailable = p.Available && !p.Disabled,
            AvailableQuantity = p.AvailableQuantity,
            TenantId = p.TenantId // Dodaj TenantId do DTO, żeby klient wiedział z jakiej wypożyczalni jest produkt
        })
        .ToListAsync(ct);
    return Results.Ok(products);
})
.WithName("GetProducts")
.WithTags("Products")
.WithSummary("Lista produktów")
.WithDescription("Zwraca listę produktów. Bez nagłówka X-Tenant-Id zwraca produkty ze wszystkich wypożyczalni. Z nagłówkiem - tylko z danej wypożyczalni.")
.Produces<List<ProductDto>>(StatusCodes.Status200OK);

// Product by Id
app.MapGet("/api/products/{id:guid}", async (HttpRequest http, ApplicationDbContext db, Guid id, CancellationToken ct) =>
{
    var tenantId = GetTenantId(http);
    var query = db.Products.AsQueryable();
    if (tenantId != Guid.Empty)
    {
        query = query.Where(x => x.TenantId == tenantId);
    }

    var product = await query
        .Where(x => x.Id == id)
        .Select(x => new ProductDto
        {
            Id = x.Id,
            TenantId = x.TenantId,
            Name = x.Name,
            Sku = x.Sku,
            Category = x.Category,
            ImageUrl = x.ImageUrl,
            ImageBasePath = x.ImageBasePath,
            DailyPrice = x.DailyPrice,
            Description = x.Description,
            FullImageUrl = x.ImageUrl,
            IsAvailable = x.Available && !x.Disabled,
            AvailableQuantity = x.AvailableQuantity
        })
        .FirstOrDefaultAsync(ct);
    return product is null ? Results.NotFound() : Results.Ok(product);
})
.WithName("GetProductById")
.WithTags("Products")
.WithSummary("Szczegoly produktu")
.WithDescription("Zwraca pojedynczy produkt po Id. Bez naglowka X-Tenant-Id zwraca dowolny produkt, z naglowkiem ogranicza do wskazanej wypozyczalni.")
.Produces<ProductDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// Holds
app.MapPost("/api/holds", async (HttpRequest http, ApplicationDbContext db, CreateHoldRequest req, CancellationToken ct) =>
{
    var product = await db.Products.FirstOrDefaultAsync(p => p.Id == req.ProductId, ct);
    if (product is null)
    {
        return Results.NotFound(new { error = "Produkt nie istnieje." });
    }

    var ttlMinutes = req.TtlMinutes ?? 15;
    var hold = new ReservationHold
    {
        Id = Guid.NewGuid(),
        TenantId = product.TenantId,
        ProductId = req.ProductId,
        Quantity = req.Quantity,
        StartDateUtc = req.StartDateUtc,
        EndDateUtc = req.EndDateUtc,
        CreatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(ttlMinutes),
        CustomerId = req.CustomerId,
        SessionId = req.SessionId
    };

    db.ReservationHolds.Add(hold);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new CreateHoldResponse { Id = hold.Id, ExpiresAtUtc = hold.ExpiresAtUtc });
})
.WithName("CreateHold")
.WithTags("Holds")
.WithSummary("Utwórz rezerwację tymczasową (hold)")
.WithDescription("Tworzy hold na produkt w określonym przedziale czasu. Hold wygasa automatycznie po TTL.")
.Accepts<CreateHoldRequest>("application/json")
.Produces<CreateHoldResponse>(StatusCodes.Status200OK)
.AllowAnonymous();

app.MapDelete("/api/holds/{id:guid}", async (HttpRequest http, ApplicationDbContext db, Guid id, CancellationToken ct) =>
{
    var hold = await db.ReservationHolds.FirstOrDefaultAsync(h => h.Id == id, ct);
    if (hold is null) return Results.NotFound();
    db.ReservationHolds.Remove(hold);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
})
.WithName("DeleteHold")
.WithTags("Holds")
.WithSummary("Usuń hold")
.WithDescription("Usuwa istniejący hold. Zwraca 404, gdy nie istnieje.")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.AllowAnonymous();

// Customers
app.MapPost("/api/customers", async (HttpRequest http, ApplicationDbContext db, CreateCustomerRequest req, CancellationToken ct) =>
{
    var tenantId = GetTenantId(http);
    var normalizedEmail = req.Email?.Trim();

    if (!string.IsNullOrEmpty(normalizedEmail))
    {
        // For global customers (no tenant), check if email exists globally
        // For tenant-specific customers, check within tenant
        var query = db.Customers.Where(c => c.Email != null && c.Email.ToLower() == normalizedEmail.ToLower());
        if (tenantId != Guid.Empty)
        {
            query = query.Where(c => c.TenantId == tenantId);
        }
        
        var existingCustomer = await query.FirstOrDefaultAsync(ct);
        if (existingCustomer is not null)
        {
            // If customer exists, return it instead of conflict (for WASM client convenience)
            if (tenantId == Guid.Empty)
            {
                return Results.Ok(ToCustomerDto(existingCustomer));
            }
            return Results.Conflict(new { error = "Customer with the provided email already exists." });
        }
    }

    var customer = new Customer
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        FullName = req.FullName,
        Email = normalizedEmail,
        PhoneNumber = req.PhoneNumber?.Trim(),
        Address = req.Address,
        DocumentNumber = req.DocumentNumber,
        Notes = req.Notes,
        CreatedAtUtc = DateTime.UtcNow
    };

    await db.Customers.AddAsync(customer, ct);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/api/customers/{customer.Id}", ToCustomerDto(customer));
})
.WithName("CreateCustomer")
.WithTags("Customers")
.WithSummary("Utwórz klienta")
.WithDescription("Tworzy nowego klienta w kontekście tenanta.")
.Accepts<CreateCustomerRequest>("application/json")
.Produces<CustomerDto>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status409Conflict)
.AllowAnonymous();

app.MapPut("/api/customers/{id:guid}", async (HttpRequest http, ApplicationDbContext db, Guid id, CreateCustomerRequest req, CancellationToken ct) =>
{
    var tenantId = GetTenantId(http);
    
    // First try to find by ID only (for global customers or cross-tenant updates)
    var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
    
    // If not found, or if tenant-specific and customer belongs to different tenant, deny access
    if (customer is null)
    {
        return Results.NotFound();
    }
    
    // Security check: if request has tenant, customer must belong to that tenant OR be global (TenantId = Empty)
    if (tenantId != Guid.Empty && customer.TenantId != Guid.Empty && customer.TenantId != tenantId)
    {
        return Results.NotFound(); // Don't reveal that customer exists in another tenant
    }

    var normalizedEmail = req.Email?.Trim();
    // Only check for email conflict if email is being changed
    var emailChanged = !string.Equals(customer.Email?.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase);
    if (!string.IsNullOrEmpty(normalizedEmail) && emailChanged)
    {
        // Check for email conflict within tenant (or globally if no tenant)
        var conflictQuery = db.Customers.Where(c => c.Id != id && c.Email != null && c.Email.ToLower() == normalizedEmail.ToLower());
        if (tenantId != Guid.Empty)
        {
            conflictQuery = conflictQuery.Where(c => c.TenantId == tenantId);
        }
        
        if (await conflictQuery.AnyAsync(ct))
        {
            return Results.Conflict(new { error = "Customer with the provided email already exists." });
        }
    }

    customer.FullName = req.FullName;
    customer.Email = normalizedEmail;
    customer.PhoneNumber = req.PhoneNumber?.Trim();
    customer.Address = req.Address;
    customer.DocumentNumber = req.DocumentNumber;
    customer.Notes = req.Notes;

    await db.SaveChangesAsync(ct);

    return Results.Ok(ToCustomerDto(customer));
})
.WithName("UpdateCustomer")
.WithTags("Customers")
.WithSummary("Aktualizuj dane klienta")
.WithDescription("Aktualizuje istniejącego klienta dla danego tenanta.")
.Accepts<CreateCustomerRequest>("application/json")
.Produces<CustomerDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict)
.AllowAnonymous();

app.MapGet("/api/customers/{id:guid}", async (HttpRequest http, ApplicationDbContext db, Guid id, CancellationToken ct) =>
{
    var tenantId = GetTenantId(http);
    
    // For global customers (no tenant), find by ID only
    // For tenant-specific, filter by tenant
    var query = db.Customers.Where(c => c.Id == id);
    if (tenantId != Guid.Empty)
    {
        query = query.Where(c => c.TenantId == tenantId);
    }
    
    var customer = await query.FirstOrDefaultAsync(ct);
    return customer is null ? Results.NotFound() : Results.Ok(ToCustomerDto(customer));
})
.WithName("GetCustomerById")
.WithTags("Customers")
.WithSummary("Pobierz klienta")
.WithDescription("Zwraca klienta po identyfikatorze.")
.Produces<CustomerDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.AllowAnonymous();

app.MapGet("/api/customers/by-email", async (HttpRequest http, ApplicationDbContext db, string? email, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(email))
    {
        return Results.BadRequest(new { error = "Email query parameter is required." });
    }

    var tenantId = GetTenantId(http);
    var normalizedEmail = email.Trim().ToLower();
    
    // For global search (no tenant), search across all customers
    // For tenant-specific search, filter by tenant
    var query = db.Customers.Where(c => c.Email != null && c.Email.ToLower() == normalizedEmail);
    if (tenantId != Guid.Empty)
    {
        query = query.Where(c => c.TenantId == tenantId);
    }
    
    var customer = await query.FirstOrDefaultAsync(ct);

    return customer is null ? Results.NotFound() : Results.Ok(ToCustomerDto(customer));
})
.WithName("GetCustomerByEmail")
.WithTags("Customers")
.WithSummary("Pobierz klienta po emailu")
.WithDescription("Zwraca klienta w oparciu o adres email w ramach danego tenanta.")
.Produces<CustomerDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status400BadRequest)
.AllowAnonymous();

// Payments
app.MapPost("/api/payments/quote", async (HttpRequest http, ApplicationDbContext db, PaymentQuoteRequest req, CancellationToken ct) =>
{
    var tenantId = GetTenantId(http);
    try
    {
        var computation = await PaymentCalculator.ComputeAsync(tenantId, req, db, ct);
        return Results.Ok(new PaymentQuoteResponse
        {
            TotalAmount = computation.TotalAmount,
            DepositAmount = computation.DepositAmount,
            Currency = "PLN",
            RentalDays = computation.RentalDays,
            Tenants = computation.Tenants
                .Select(t => new TenantQuoteBreakdown
                {
                    TenantId = t.TenantId,
                    TotalAmount = t.TotalAmount,
                    DepositAmount = t.DepositAmount
                })
                .ToList()
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("GetPaymentQuote")
.WithTags("Payments")
.WithSummary("Oblicz koszty rezerwacji")
.WithDescription("Zwraca wyliczoną kwotę i depozyt dla wskazanych produktów i zakresu dat.")
.Accepts<PaymentQuoteRequest>("application/json")
.Produces<PaymentQuoteResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.AllowAnonymous();

app.MapPost("/api/payments/intents", async (HttpRequest http, ApplicationDbContext db, IPaymentGateway gateway, CreatePaymentIntentRequest req, CancellationToken ct) =>
{
    var tenantId = GetTenantId(http);
    try
    {
        var quoteRequest = new PaymentQuoteRequest
        {
            StartDateUtc = req.StartDateUtc,
            EndDateUtc = req.EndDateUtc,
            Items = req.Items
        };

        var computation = await PaymentCalculator.ComputeAsync(tenantId, quoteRequest, db, ct);
        if (computation.Tenants.Count == 0)
        {
            return Results.BadRequest(new { error = "Brak pozycji do wyliczenia platnosci." });
        }
        var currency = string.IsNullOrWhiteSpace(req.Currency) ? "PLN" : req.Currency;
        var tenantIds = computation.Tenants.Select(t => t.TenantId).Distinct().ToList();
        var paymentTenant = tenantIds.Count == 1 ? tenantIds[0] : Guid.Empty;
        
        // Add rental metadata for Stripe
        var metadata = new Dictionary<string, string>
        {
            ["tenant_id"] = paymentTenant.ToString(),
            ["tenant_ids"] = string.Join(",", tenantIds),
            ["rental_start"] = req.StartDateUtc.ToString("O"),
            ["rental_end"] = req.EndDateUtc.ToString("O"),
            ["items_count"] = req.Items.Count.ToString(),
            ["rental_days"] = computation.RentalDays.ToString(),
            ["idempotency_key"] = $"pi:{Guid.NewGuid():N}"
        };
        
        var intent = await gateway.CreatePaymentIntentAsync(paymentTenant, computation.TotalAmount, computation.DepositAmount, currency, metadata);
        return Results.Ok(intent);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("CreatePaymentIntent")
.WithTags("Payments")
.WithSummary("Utwórz zamiar płatności")
.WithDescription("Tworzy Stripe PaymentIntent dla wskazanej rezerwacji (deposit + total).")
.Accepts<CreatePaymentIntentRequest>("application/json")
.Produces<PaymentIntentDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.AllowAnonymous();

app.MapGet("/api/payments/intents/{id}", async (HttpRequest http, IPaymentGateway gateway, string id) =>
{
    var tenantId = GetTenantId(http);
    var intent = await gateway.GetPaymentIntentAsync(tenantId, id);
    return intent is null ? Results.NotFound() : Results.Ok(intent);
})
.WithName("GetPaymentIntent")
.WithTags("Payments")
.WithSummary("Pobierz PaymentIntent")
.WithDescription("Zwraca szczegóły PaymentIntentu dla danego tenanta.")
.Produces<PaymentIntentDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.AllowAnonymous();

// My rentals
app.MapGet("/api/my-rentals", async (HttpRequest http, ApplicationDbContext db, string? status, DateTime? from, DateTime? to, Guid? customerId, CancellationToken ct) =>
{
    var tenantId = GetTenantId(http);
    var now = DateTime.UtcNow;

    var query = db.Rentals
        .Where(r => r.TenantId == tenantId)
        .Include(r => r.Items)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RentalStatus>(status, true, out var parsed))
        query = query.Where(r => r.Status == parsed);
    if (from.HasValue)
        query = query.Where(r => r.StartDateUtc >= from.Value);
    if (to.HasValue)
        query = query.Where(r => r.EndDateUtc <= to.Value);
    if (customerId.HasValue)
        query = query.Where(r => r.CustomerId == customerId.Value);

    var list = await query
        .OrderByDescending(r => r.CreatedAtUtc)
        .Select(r => new MyRentalDto
        {
            Id = r.Id,
            Title = r.Notes ?? "Rental",
            StartDateUtc = r.StartDateUtc,
            EndDateUtc = r.EndDateUtc,
            Quantity = r.Items.Sum(i => i.Quantity),
            TotalAmount = r.TotalAmount,
            DepositAmount = r.DepositAmount,
            PaymentStatus = r.PaymentStatus,
            Status = r.Status.ToString(),
            CanCancel = (r.Status == RentalStatus.Pending || r.Status == RentalStatus.Confirmed) && r.StartDateUtc > now
        })
        .ToListAsync(ct);

    return Results.Ok(list);
})
.WithName("GetMyRentals")
.WithTags("Rentals")
.WithSummary("Moje wynajmy")
.WithDescription("Zwraca listę wynajmów z opcjonalnymi filtrami: status, from (UTC), to (UTC).")
.Produces<List<MyRentalDto>>(StatusCodes.Status200OK)
.RequireAuthorization();

// Rentals
app.MapPost("/api/rentals", async (HttpRequest http, ApplicationDbContext db, IPaymentGateway gateway, CreateRentalRequest req, CancellationToken ct) =>
{
    var tenantId = GetTenantId(http);

    if (req.EndDateUtc <= req.StartDateUtc)
    {
        return Results.BadRequest(new { error = "EndDateUtc must be after StartDateUtc." });
    }

    if (!string.IsNullOrWhiteSpace(req.IdempotencyKey))
    {
        var existing = await db.Rentals
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == req.IdempotencyKey, ct);

        if (existing is not null)
        {
            return Results.Ok(ToRentalResponse(existing));
        }
    }

    var intent = await gateway.GetPaymentIntentAsync(tenantId, req.PaymentIntentId);
    if (intent is null)
    {
        return Results.BadRequest(new { error = "Payment intent not found or expired." });
    }

    PaymentComputationResult computation;
        try
        {
            computation = await PaymentCalculator.ComputeAsync(tenantId, new PaymentQuoteRequest
            {
                StartDateUtc = req.StartDateUtc,
                EndDateUtc = req.EndDateUtc,
                Items = req.Items
            }, db, ct, allowMixedTenants: false);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

    if (intent.Amount != computation.TotalAmount)
    {
        return Results.BadRequest(new { error = "Payment intent amount does not match the computed total." });
    }

    var rental = new Rental
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        CustomerId = req.CustomerId,
        StartDateUtc = req.StartDateUtc,
        EndDateUtc = req.EndDateUtc,
        Notes = req.Notes,
        IdempotencyKey = req.IdempotencyKey,
        Status = RentalStatus.Pending,
        CreatedAtUtc = DateTime.UtcNow,
        TotalAmount = computation.TotalAmount,
        DepositAmount = computation.DepositAmount,
        PaymentIntentId = intent.Id,
        PaymentStatus = intent.Status
    };

    foreach (var item in req.Items)
    {
        var pricePerDay = computation.ProductPrices[item.ProductId];
        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rental.Id,
            ProductId = item.ProductId,
            Quantity = item.Quantity,
            PricePerDay = pricePerDay,
            Subtotal = pricePerDay * item.Quantity * computation.RentalDays
        });
    }

    db.Rentals.Add(rental);
    await db.SaveChangesAsync(ct);

    // Capture payment and update status
    var captureSuccess = await gateway.CapturePaymentAsync(tenantId, intent.Id);
    if (captureSuccess)
    {
        var updatedIntent = await gateway.GetPaymentIntentAsync(tenantId, intent.Id);
        if (updatedIntent != null)
        {
            rental.PaymentStatus = updatedIntent.Status;
            await db.SaveChangesAsync(ct);
        }
    }

    return Results.Ok(ToRentalResponse(rental));
})
.WithName("CreateRental")
.WithTags("Rentals")
.WithSummary("Utwórz wynajem")
.WithDescription("Tworzy nowy wynajem na podstawie pozycji i dat. Kwota wyliczana z DailyPrice x Ilość x Dni.")
.Accepts<CreateRentalRequest>("application/json")
.Produces<RentalResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.RequireAuthorization();

app.MapDelete("/api/rentals/{id:guid}", async (HttpRequest http, ApplicationDbContext db, Guid id, CancellationToken ct) =>
{
    var tenantId = GetTenantId(http);
    var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
    if (rental is null) return Results.NotFound();
    rental.Status = RentalStatus.Cancelled;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
})
.WithName("CancelRental")
.WithTags("Rentals")
.WithSummary("Anuluj wynajem")
.WithDescription("Ustawia status wynajmu na Cancelled. Zwraca 404, jeśli nie istnieje.")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.RequireAuthorization();

static CustomerDto ToCustomerDto(Customer customer)
{
    return new CustomerDto
    {
        Id = customer.Id,
        FullName = customer.FullName,
        Email = customer.Email ?? string.Empty,
        PhoneNumber = customer.PhoneNumber ?? string.Empty,
        Address = customer.Address,
        DocumentNumber = customer.DocumentNumber,
        Notes = customer.Notes
    };
}

static RentalResponse ToRentalResponse(Rental rental)
{
    return new RentalResponse
    {
        Id = rental.Id,
        TotalAmount = rental.TotalAmount,
        ContractUrl = rental.ContractUrl,
        Status = rental.Status.ToString(),
        DepositAmount = rental.DepositAmount,
        PaymentStatus = rental.PaymentStatus
    };
}

// Map Authentication Endpoints
app.MapAuthEndpoints();

// Map Tenant Endpoints (public, no auth)
app.MapTenantEndpoints();

// Map Stripe Webhook Endpoints
app.MapStripeWebhookEndpoints();

// Map Stripe Checkout Endpoints (bez JS!)
app.MapStripeCheckoutEndpoints();

app.Run();

public partial class Program
{
}
