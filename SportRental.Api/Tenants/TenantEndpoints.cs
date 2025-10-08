using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;

namespace SportRental.Api.Tenants;

public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants").WithTags("Tenants");

        group.MapGet("/", GetAvailableTenants)
            .WithName("GetAvailableTenants")
            .WithDescription("Pobiera listę dostępnych wypożyczalni (tenantów)")
            .Produces<List<TenantDto>>(200)
            .AllowAnonymous(); // Public endpoint - no auth required
    }

    private static async Task<IResult> GetAvailableTenants(
        [FromServices] ApplicationDbContext dbContext)
    {
        var tenants = await dbContext.Tenants
            .Select(t => new TenantDto
            {
                Id = t.Id,
                Name = t.Name,
                LogoUrl = t.LogoUrl,
                PrimaryColor = t.PrimaryColorHex,
                SecondaryColor = t.SecondaryColorHex
            })
            .ToListAsync();

        return Results.Ok(tenants);
    }
}

public sealed record TenantDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? LogoUrl { get; init; }
    public string? PrimaryColor { get; init; }
    public string? SecondaryColor { get; init; }
}
