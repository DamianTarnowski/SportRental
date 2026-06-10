using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services;

/// <summary>
/// Faza 9b — vouchery / karty podarunkowe.
/// </summary>
public interface IVoucherService
{
    Task<Voucher> CreateAsync(Guid tenantId, decimal balance, string? toName, string? toEmail,
        DateOnly? expires, Guid? createdByUserId, CancellationToken ct = default);
    Task<VoucherValidationResult> ValidateAsync(string code, CancellationToken ct = default);
    Task<bool> RedeemAsync(Guid voucherId, Guid rentalId, Guid tenantId, decimal amount, CancellationToken ct = default);
    Task<IReadOnlyList<Voucher>> ListAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed record VoucherValidationResult(
    bool IsValid,
    Guid? VoucherId,
    decimal RemainingBalance,
    string? Reason);

public sealed class VoucherService : IVoucherService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<VoucherService> _logger;

    public VoucherService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<VoucherService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<Voucher> CreateAsync(Guid tenantId, decimal balance, string? toName, string? toEmail,
        DateOnly? expires, Guid? createdByUserId, CancellationToken ct = default)
    {
        if (balance <= 0) throw new ArgumentException("Saldo musi być dodatnie.", nameof(balance));

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        // Generuj unique code z retry (kolizja mało prawdopodobna ale możliwa)
        string code;
        for (int attempt = 0; ; attempt++)
        {
            code = GenerateCode();
            var taken = await db.Vouchers.IgnoreQueryFilters().AnyAsync(v => v.Code == code, ct);
            if (!taken) break;
            if (attempt > 5) throw new InvalidOperationException("Nie udało się wygenerować unikalnego kodu.");
        }

        var voucher = new Voucher
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            IssuedToName = toName,
            IssuedToEmail = toEmail,
            InitialBalance = balance,
            RemainingBalance = balance,
            ExpiresAt = expires,
            Status = VoucherStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = createdByUserId
        };
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Voucher {Code} created with balance {Balance}", code, balance);
        return voucher;
    }

    public async Task<VoucherValidationResult> ValidateAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new VoucherValidationResult(false, null, 0, "Pusty kod.");

        var normalized = code.Trim().ToUpperInvariant();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        // Vouchery są globally-unique cross-tenant, więc IgnoreQueryFilters tutaj
        var voucher = await db.Vouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Code == normalized, ct);

        if (voucher == null)
            return new VoucherValidationResult(false, null, 0, "Voucher nieznany.");

        if (voucher.Status == VoucherStatus.Cancelled)
            return new VoucherValidationResult(false, null, 0, "Voucher anulowany.");
        if (voucher.Status == VoucherStatus.Expired)
            return new VoucherValidationResult(false, null, 0, "Voucher wygasł.");
        if (voucher.Status == VoucherStatus.FullyRedeemed)
            return new VoucherValidationResult(false, null, 0, "Voucher w pełni wykorzystany.");

        if (voucher.ExpiresAt.HasValue && DateOnly.FromDateTime(DateTime.UtcNow) > voucher.ExpiresAt.Value)
            return new VoucherValidationResult(false, voucher.Id, 0,
                $"Voucher wygasł {voucher.ExpiresAt.Value:dd.MM.yyyy}.");

        if (voucher.RemainingBalance <= 0)
            return new VoucherValidationResult(false, voucher.Id, 0, "Saldo zerowe.");

        return new VoucherValidationResult(true, voucher.Id, voucher.RemainingBalance, null);
    }

    public async Task<bool> RedeemAsync(Guid voucherId, Guid rentalId, Guid tenantId, decimal amount, CancellationToken ct = default)
    {
        if (amount <= 0) return false;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var voucher = await db.Vouchers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == voucherId, ct);
        if (voucher == null) return false;

        if (voucher.RemainingBalance < amount)
            amount = voucher.RemainingBalance; // wykorzystaj ile jest

        // Idempotency
        var exists = await db.VoucherRedemptions
            .IgnoreQueryFilters()
            .AnyAsync(r => r.VoucherId == voucherId && r.RentalId == rentalId, ct);
        if (exists) return true;

        db.VoucherRedemptions.Add(new VoucherRedemption
        {
            Id = Guid.NewGuid(),
            VoucherId = voucherId,
            RentalId = rentalId,
            TenantId = tenantId,
            RedeemedAtUtc = DateTime.UtcNow,
            Amount = amount
        });
        voucher.RemainingBalance -= amount;
        if (voucher.RemainingBalance <= 0.001m)
        {
            voucher.RemainingBalance = 0;
            voucher.Status = VoucherStatus.FullyRedeemed;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Voucher {Code} redeemed {Amount} for rental {RentalId}, remaining {Balance}",
            voucher.Code, amount, rentalId, voucher.RemainingBalance);
        return true;
    }

    public async Task<IReadOnlyList<Voucher>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);
        return await db.Vouchers
            .AsNoTracking()
            .OrderByDescending(v => v.CreatedAtUtc)
            .ToListAsync(ct);
    }

    /// <summary>VCH-XXXX-XXXX-XXXX (16 znaków alfanumerycznych z myślnikami)</summary>
    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // bez 0/O/I/1 dla czytelności
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        var sb = new System.Text.StringBuilder("VCH-");
        for (int i = 0; i < 16; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append('-');
            sb.Append(chars[bytes[i] % chars.Length]);
        }
        return sb.ToString();
    }
}
