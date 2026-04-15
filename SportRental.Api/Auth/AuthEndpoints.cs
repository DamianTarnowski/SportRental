using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using System.Security.Cryptography;

namespace SportRental.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/register", Register)
            .WithName("RegisterClient")
            .WithDescription("Rejestracja nowego klienta")
            .Produces<AuthResponse>(200)
            .Produces<ProblemDetails>(400);

        group.MapPost("/login", Login)
            .WithName("LoginClient")
            .WithDescription("Logowanie klienta (email + hasło)")
            .Produces<AuthResponse>(200)
            .Produces<ProblemDetails>(401);

        group.MapPost("/refresh", RefreshToken)
            .WithName("RefreshToken")
            .WithDescription("Odświeżenie access token używając refresh token")
            .Produces<AuthResponse>(200)
            .Produces<ProblemDetails>(401);

        group.MapPost("/revoke", RevokeToken)
            .WithName("RevokeToken")
            .WithDescription("Unieważnienie refresh token (logout)")
            .Produces(204)
            .Produces<ProblemDetails>(400);

        group.MapPost("/guest-session", GuestSession)
            .WithName("CreateGuestSession")
            .WithDescription("Tworzy sesję gościa (customer + krótki JWT) do checkout-u bez rejestracji konta.")
            .Produces<GuestSessionResponse>(200)
            .Produces<ProblemDetails>(400)
            .Produces<ProblemDetails>(409);
    }

    private static async Task<IResult> GuestSession(
        [FromBody] GuestSessionRequest request,
        [FromServices] ApplicationDbContext dbContext,
        [FromServices] JwtTokenService jwtTokenService,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest(new { error = "FullName i Email są wymagane." });
        }

        var normalizedEmail = request.Email.Trim();
        var emailLower = normalizedEmail.ToLower();

        // Tenant z headera lub Guid.Empty (marketplace cross-tenant)
        var tenantIdHeader = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        Guid tenantId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(tenantIdHeader))
        {
            if (!Guid.TryParse(tenantIdHeader, out tenantId))
            {
                return Results.BadRequest(new { error = "Nieprawidłowy X-Tenant-Id." });
            }
        }

        // Jeśli email należy do zarejestrowanego konta (ApplicationUser), nie wolno wydać gościa bez hasła.
        var accountExists = await dbContext.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == emailLower);
        if (accountExists)
        {
            return Results.Conflict(new { error = "Dla tego emaila istnieje już konto użytkownika. Zaloguj się." });
        }

        // Szukaj istniejącego gościa (Customer bez konta Identity) po email w tenancie (lub globalnie).
        var customerQuery = dbContext.Customers.Where(c => c.Email != null && c.Email.ToLower() == emailLower);
        if (tenantId != Guid.Empty)
        {
            customerQuery = customerQuery.Where(c => c.TenantId == tenantId || c.TenantId == Guid.Empty);
        }
        var customer = await customerQuery.OrderByDescending(c => c.CreatedAtUtc).FirstOrDefaultAsync();

        if (customer is null)
        {
            customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FullName = request.FullName.Trim(),
                Email = normalizedEmail,
                PhoneNumber = request.PhoneNumber?.Trim(),
                Address = request.Address,
                DocumentNumber = request.DocumentNumber,
                Notes = request.Notes,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.Customers.Add(customer);
            await dbContext.SaveChangesAsync();
        }
        else
        {
            // Nie ujawniamy danych istniejącego klienta — odświeżamy tylko pola z requestu,
            // które gość sam podał (imię/telefon/adres), i wydajemy token sesji.
            customer.FullName = request.FullName.Trim();
            if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
                customer.PhoneNumber = request.PhoneNumber.Trim();
            if (!string.IsNullOrWhiteSpace(request.Address))
                customer.Address = request.Address;
            if (!string.IsNullOrWhiteSpace(request.DocumentNumber))
                customer.DocumentNumber = request.DocumentNumber;
            if (!string.IsNullOrWhiteSpace(request.Notes))
                customer.Notes = request.Notes;
            await dbContext.SaveChangesAsync();
        }

        var token = jwtTokenService.CreateGuestToken(customer.Id, customer.TenantId, customer.Email!);

        return Results.Ok(new GuestSessionResponse
        {
            AccessToken = token.AccessToken,
            ExpiresIn = (int)(token.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds,
            TokenType = "Bearer",
            CustomerId = customer.Id,
            TenantId = customer.TenantId,
            Email = customer.Email ?? string.Empty,
            FullName = customer.FullName
        });
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterRequest request,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] ApplicationDbContext dbContext,
        [FromServices] JwtTokenService jwtTokenService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "Email i hasło są wymagane" });
        }

        // Get tenant from header (consistent with rest of API)
        var tenantIdHeader = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tenantIdHeader) || !Guid.TryParse(tenantIdHeader, out var tenantId))
        {
            return Results.BadRequest(new { error = "Header X-Tenant-Id jest wymagany" });
        }

        // Verify tenant exists
        var tenantExists = await dbContext.Tenants.AnyAsync(t => t.Id == tenantId);
        if (!tenantExists)
        {
            return Results.BadRequest(new { error = "Nieprawidłowy Tenant ID" });
        }

        // Check if email already exists
        var existingUser = await userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return Results.BadRequest(new { error = "Email już jest zarejestrowany" });
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            TenantId = tenantId,
            // In development, auto-confirm email. In production, require email confirmation.
            EmailConfirmed = configuration.GetValue<bool>("Email:AutoConfirm") || 
                           Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return Results.BadRequest(new { error = errors });
        }

        // Assign Client role
        await userManager.AddToRoleAsync(user, "Client");

        // Automatically create Customer record for this user
        var customer = new SportRental.Infrastructure.Domain.Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = request.FullName ?? request.Email.Split('@')[0], // Use email prefix if no name provided
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            DocumentNumber = request.DocumentNumber,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();

        // Generate tokens (embed customer-id claim so downstream endpoints can scope data)
        var (accessToken, refreshToken) = await GenerateTokens(user, tenantId, new[] { "Client" }, jwtTokenService, dbContext, customer.Id);

        return Results.Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = jwtTokenService.GetAccessTokenLifetimeSeconds(),
            TokenType = "Bearer",
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email!,
                TenantId = tenantId
            }
        });
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] ApplicationDbContext dbContext,
        [FromServices] JwtTokenService jwtTokenService,
        [FromServices] IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "Email i hasło są wymagane" });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
            {
                return Results.Unauthorized(); // W produkcji zwróć informację o blockadie
            }
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        var tenantId = user.TenantId ?? Guid.Empty;
        var customerId = await LookupCustomerIdAsync(dbContext, user, tenantId);

        var (accessToken, refreshToken) = await GenerateTokens(user, tenantId, roles, jwtTokenService, dbContext, customerId);

        return Results.Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = jwtTokenService.GetAccessTokenLifetimeSeconds(),
            TokenType = "Bearer",
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email!,
                TenantId = tenantId
            }
        });
    }

    private static async Task<IResult> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        [FromServices] ApplicationDbContext dbContext,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] JwtTokenService jwtTokenService)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.BadRequest(new { error = "Refresh token jest wymagany" });
        }

        var storedToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        if (storedToken == null || !storedToken.IsActive)
        {
            return Results.Unauthorized();
        }

        // Revoke old token
        storedToken.IsRevoked = true;
        storedToken.RevokedAtUtc = DateTime.UtcNow;
        storedToken.RevokedReason = "Replaced by new token";

        var user = await userManager.FindByIdAsync(storedToken.UserId.ToString());
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        var tenantId = user.TenantId ?? Guid.Empty;
        var customerId = await LookupCustomerIdAsync(dbContext, user, tenantId);

        var (accessToken, newRefreshToken) = await GenerateTokens(user, tenantId, roles, jwtTokenService, dbContext, customerId);

        storedToken.ReplacedByToken = newRefreshToken;
        await dbContext.SaveChangesAsync();

        return Results.Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresIn = jwtTokenService.GetAccessTokenLifetimeSeconds(),
            TokenType = "Bearer",
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email!,
                TenantId = tenantId
            }
        });
    }

    private static async Task<IResult> RevokeToken(
        [FromBody] RevokeTokenRequest request,
        [FromServices] ApplicationDbContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.BadRequest(new { error = "Refresh token jest wymagany" });
        }

        var storedToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        if (storedToken != null && storedToken.IsActive)
        {
            storedToken.IsRevoked = true;
            storedToken.RevokedAtUtc = DateTime.UtcNow;
            storedToken.RevokedReason = "User logout";
            await dbContext.SaveChangesAsync();
        }

        return Results.NoContent();
    }

    private static async Task<Guid?> LookupCustomerIdAsync(ApplicationDbContext dbContext, ApplicationUser user, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return null;
        var normalized = user.Email.Trim().ToLower();
        var query = dbContext.Customers.Where(c => c.Email != null && c.Email.ToLower() == normalized);
        if (tenantId != Guid.Empty)
        {
            query = query.Where(c => c.TenantId == tenantId || c.TenantId == Guid.Empty);
        }
        var customer = await query.OrderByDescending(c => c.CreatedAtUtc).FirstOrDefaultAsync();
        return customer?.Id;
    }

    private static async Task<(string accessToken, string refreshToken)> GenerateTokens(
        ApplicationUser user,
        Guid tenantId,
        IEnumerable<string> roles,
        JwtTokenService jwtTokenService,
        ApplicationDbContext dbContext,
        Guid? customerId = null)
    {
        var tokenResult = jwtTokenService.CreateToken(user, tenantId, roles, customerId);
        var refreshTokenString = GenerateRefreshTokenString();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenString,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(jwtTokenService.GetRefreshTokenLifetimeDays()),
            IsRevoked = false
        };

        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync();

        return (tokenResult.AccessToken, refreshTokenString);
    }

    private static string GenerateRefreshTokenString()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
