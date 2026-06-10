using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services;

/// <summary>
/// Faza 9a — walidacja + naliczenie zniżki + atomowa rejestracja redemption.
/// </summary>
public interface IDiscountService
{
    Task<DiscountValidationResult> ValidateAsync(Guid tenantId, string code, decimal orderAmount, CancellationToken ct = default);
    Task<bool> RedeemAsync(Guid tenantId, Guid discountCodeId, Guid rentalId, decimal appliedAmount, CancellationToken ct = default);
}

public sealed record DiscountValidationResult(
    bool IsValid,
    Guid? DiscountCodeId,
    decimal DiscountAmount,
    string? Reason);

public sealed class DiscountService : IDiscountService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<DiscountService> _logger;

    public DiscountService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<DiscountService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<DiscountValidationResult> ValidateAsync(Guid tenantId, string code, decimal orderAmount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new DiscountValidationResult(false, null, 0, "Pusty kod.");

        var normalized = code.Trim().ToUpperInvariant();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var disc = await db.DiscountCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == normalized, ct);

        if (disc == null)
            return new DiscountValidationResult(false, null, 0, "Kod nieznany.");

        if (!disc.IsActive)
            return new DiscountValidationResult(false, null, 0, "Kod nieaktywny.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (disc.ValidFrom.HasValue && today < disc.ValidFrom.Value)
            return new DiscountValidationResult(false, null, 0, $"Kod aktywny od {disc.ValidFrom.Value:dd.MM.yyyy}.");
        if (disc.ValidTo.HasValue && today > disc.ValidTo.Value)
            return new DiscountValidationResult(false, null, 0, $"Kod wygasł {disc.ValidTo.Value:dd.MM.yyyy}.");

        if (disc.MaxUses.HasValue && disc.UsedCount >= disc.MaxUses.Value)
            return new DiscountValidationResult(false, null, 0, "Limit użyć kodu wyczerpany.");

        if (disc.MinOrderAmount.HasValue && orderAmount < disc.MinOrderAmount.Value)
            return new DiscountValidationResult(false, null, 0,
                $"Min. wartość zamówienia dla tego kodu: {disc.MinOrderAmount.Value:N2} zł.");

        var amount = disc.Type switch
        {
            DiscountType.Percentage => Math.Round(orderAmount * (disc.Value / 100m), 2),
            DiscountType.FixedAmount => Math.Min(disc.Value, orderAmount),
            _ => 0m
        };

        return new DiscountValidationResult(true, disc.Id, amount, null);
    }

    public async Task<bool> RedeemAsync(Guid tenantId, Guid discountCodeId, Guid rentalId, decimal appliedAmount, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var disc = await db.DiscountCodes
            .FirstOrDefaultAsync(d => d.Id == discountCodeId, ct);
        if (disc == null) return false;

        // Idempotency — czy już zapisane?
        var exists = await db.DiscountRedemptions
            .AnyAsync(r => r.DiscountCodeId == discountCodeId && r.RentalId == rentalId, ct);
        if (exists) return true;

        db.DiscountRedemptions.Add(new DiscountRedemption
        {
            Id = Guid.NewGuid(),
            DiscountCodeId = discountCodeId,
            RentalId = rentalId,
            TenantId = tenantId,
            RedeemedAtUtc = DateTime.UtcNow,
            AppliedAmount = appliedAmount
        });
        disc.UsedCount++;

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Discount {Code} redeemed for rental {RentalId} (amount {Amount})",
            disc.Code, rentalId, appliedAmount);
        return true;
    }
}
