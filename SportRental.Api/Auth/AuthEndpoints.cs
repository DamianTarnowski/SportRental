using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
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

        // Generate tokens
        var (accessToken, refreshToken) = await GenerateTokens(user, tenantId, new[] { "Client" }, jwtTokenService, dbContext);

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

        var (accessToken, refreshToken) = await GenerateTokens(user, tenantId, roles, jwtTokenService, dbContext);

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

        var storedToken = await dbContext.Set<Auth.RefreshToken>()
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

        var (accessToken, newRefreshToken) = await GenerateTokens(user, tenantId, roles, jwtTokenService, dbContext);

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

        var storedToken = await dbContext.Set<Auth.RefreshToken>()
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

    private static async Task<(string accessToken, string refreshToken)> GenerateTokens(
        ApplicationUser user,
        Guid tenantId,
        IEnumerable<string> roles,
        JwtTokenService jwtTokenService,
        ApplicationDbContext dbContext)
    {
        var tokenResult = jwtTokenService.CreateToken(user, tenantId, roles);
        var refreshTokenString = GenerateRefreshTokenString();

        var refreshToken = new Auth.RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenString,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(jwtTokenService.GetRefreshTokenLifetimeDays()),
            IsRevoked = false
        };

        dbContext.Set<Auth.RefreshToken>().Add(refreshToken);
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
