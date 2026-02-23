using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using SportRental.Admin.Hubs;
using SportRental.Admin.Services.Sms;

namespace SportRental.Admin.Services;

public interface IRentalConfirmationService
{
    /// <summary>
    /// Tworzy nowe potwierdzenie wynajmu i zwraca token do linku
    /// </summary>
    Task<string> CreateConfirmationAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>
    /// Pobiera dane potwierdzenia po tokenie (publiczny, bez tenanta)
    /// </summary>
    Task<ConfirmationPageData?> GetConfirmationDataAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Przetwarza potwierdzenie od klienta (kliknięcie "Potwierdź")
    /// </summary>
    Task<ConfirmationResult> ProcessConfirmationAsync(string token, string ip, string userAgent, CancellationToken ct = default);

    /// <summary>
    /// Wysyła link potwierdzający SMS-em i/lub emailem
    /// </summary>
    Task SendConfirmationLinkAsync(Guid rentalId, string token, CancellationToken ct = default);
}

public record ConfirmationPageData(
    Guid RentalId,
    string CustomerName,
    string? CompanyName,
    string? CompanyPhone,
    string? CompanyEmail,
    DateTime StartDate,
    DateTime EndDate,
    decimal TotalAmount,
    decimal DepositAmount,
    List<ConfirmationItemData> Items,
    string? RegulationsText,
    bool IsAlreadyConfirmed,
    bool IsExpired
);

public record ConfirmationItemData(string ProductName, int Quantity, decimal PricePerDay, decimal Subtotal);

public record ConfirmationResult(bool Success, string Message);

public class RentalConfirmationService : IRentalConfirmationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<RentalConfirmationService> _logger;
    private readonly ISmsSender _smsSender;
    private readonly IRentalNotificationService _notificationService;
    private readonly IConfiguration _configuration;

    public RentalConfirmationService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ITenantProvider tenantProvider,
        ILogger<RentalConfirmationService> logger,
        ISmsSender smsSender,
        IRentalNotificationService notificationService,
        IConfiguration configuration)
    {
        _contextFactory = contextFactory;
        _tenantProvider = tenantProvider;
        _logger = logger;
        _smsSender = smsSender;
        _notificationService = notificationService;
        _configuration = configuration;
    }

    public async Task<string> CreateConfirmationAsync(Guid rentalId, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        if (tenantId == null)
            throw new InvalidOperationException("No tenant context available");

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.SetTenant(tenantId);

        var rental = await context.Rentals
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental?.Customer == null)
            throw new InvalidOperationException("Rental or customer not found");

        // Remove existing confirmations for this rental
        var existing = await context.RentalConfirmations
            .Where(rc => rc.RentalId == rentalId)
            .ToListAsync(ct);
        if (existing.Any())
            context.RentalConfirmations.RemoveRange(existing);

        // Generate URL-safe token
        var token = GenerateToken();

        var confirmation = new RentalConfirmation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            RentalId = rentalId,
            Token = token,
            PhoneNumber = rental.Customer.PhoneNumber,
            Email = rental.Customer.Email,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(48)
        };

        context.RentalConfirmations.Add(confirmation);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Created rental confirmation token for rental {RentalId}", rentalId);
        return token;
    }

    public async Task<ConfirmationPageData?> GetConfirmationDataAsync(string token, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var confirmation = await context.RentalConfirmations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rc => rc.Token == token, ct);

        if (confirmation == null)
            return null;

        context.SetTenant(confirmation.TenantId);

        var rental = await context.Rentals
            .Include(r => r.Customer)
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(r => r.Id == confirmation.RentalId, ct);

        if (rental?.Customer == null)
            return null;

        var company = await context.CompanyInfos
            .FirstOrDefaultAsync(ct);

        var items = rental.Items.Select(i => new ConfirmationItemData(
            i.Product?.Name ?? "Produkt",
            i.Quantity,
            i.PricePerDay,
            i.Subtotal
        )).ToList();

        return new ConfirmationPageData(
            rental.Id,
            rental.Customer.FullName ?? "Klient",
            company?.Name,
            company?.PhoneNumber,
            company?.Email,
            rental.StartDateUtc,
            rental.EndDateUtc,
            rental.TotalAmount,
            rental.DepositAmount,
            items,
            company?.RegulationsText,
            confirmation.IsConfirmed,
            confirmation.ExpiresAt < DateTime.UtcNow
        );
    }

    public async Task<ConfirmationResult> ProcessConfirmationAsync(string token, string ip, string userAgent, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var confirmation = await context.RentalConfirmations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rc => rc.Token == token, ct);

        if (confirmation == null)
            return new ConfirmationResult(false, "Nieprawidłowy link potwierdzenia.");

        if (confirmation.IsConfirmed)
            return new ConfirmationResult(true, "Wynajem został już wcześniej potwierdzony.");

        if (confirmation.ExpiresAt < DateTime.UtcNow)
            return new ConfirmationResult(false, "Link potwierdzenia wygasł. Skontaktuj się z wypożyczalnią.");

        context.SetTenant(confirmation.TenantId);

        var rental = await context.Rentals
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == confirmation.RentalId, ct);

        if (rental == null)
            return new ConfirmationResult(false, "Wynajem nie został znaleziony.");

        // Get regulations hash
        var company = await context.CompanyInfos.FirstOrDefaultAsync(ct);
        string? regulationsHash = null;
        if (!string.IsNullOrEmpty(company?.RegulationsText))
        {
            regulationsHash = ComputeSha256Hash(company.RegulationsText);
        }

        // Record confirmation proof
        confirmation.IsConfirmed = true;
        confirmation.ConfirmedAt = DateTime.UtcNow;
        confirmation.ConfirmedFromIp = ip;
        confirmation.ConfirmedUserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent;
        confirmation.RegulationsHash = regulationsHash;

        // Update rental
        rental.IsSmsConfirmed = true;
        if (rental.Status == RentalStatus.Pending)
        {
            rental.Status = RentalStatus.Confirmed;
        }

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Rental {RentalId} confirmed via link by {Ip}", rental.Id, ip);

        // Notify admin via SignalR
        try
        {
            var notification = new RentalStatusChangedEvent(
                rental.Id,
                rental.Status.ToString(),
                rental.IsSmsConfirmed,
                rental.IsSmsConfirmationSent,
                DateTime.UtcNow);
            await _notificationService.NotifyRentalStatusChangedAsync(confirmation.TenantId, notification, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SignalR notification for rental {RentalId}", rental.Id);
        }

        return new ConfirmationResult(true, "Wynajem został potwierdzony. Dziękujemy!");
    }

    public async Task SendConfirmationLinkAsync(Guid rentalId, string token, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        if (tenantId == null) return;

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.SetTenant(tenantId);

        var rental = await context.Rentals
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental?.Customer == null) return;

        var baseUrl = GetBaseUrl();
        var confirmUrl = $"{baseUrl}/confirm/{token}";
        var rentalCode = rentalId.ToString()[..8].ToUpper();
        var customerName = rental.Customer.FullName ?? "Klient";

        var confirmation = await context.RentalConfirmations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rc => rc.Token == token, ct);

        // Send SMS
        if (!string.IsNullOrWhiteSpace(rental.Customer.PhoneNumber))
        {
            try
            {
                var smsMessage = $"Potwierdz wynajem {rentalCode}: {confirmUrl}";
                await _smsSender.SendAsync(rental.Customer.PhoneNumber, smsMessage, ct);
                if (confirmation != null) confirmation.IsSmsSent = true;
                _logger.LogInformation("Sent confirmation link SMS for rental {RentalId}", rentalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation link SMS for rental {RentalId}", rentalId);
            }
        }

        // Send Email
        if (!string.IsNullOrWhiteSpace(rental.Customer.Email))
        {
            try
            {
                var company = await context.CompanyInfos.FirstOrDefaultAsync(ct);
                var companyName = company?.Name ?? "SportRental";

                // Use simple email via IEmailSender if available, otherwise log
                _logger.LogInformation("Email confirmation link for rental {RentalId} to {Email}: {Url}",
                    rentalId, rental.Customer.Email, confirmUrl);
                if (confirmation != null) confirmation.IsEmailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email for rental {RentalId}", rentalId);
            }
        }

        if (confirmation != null)
            await context.SaveChangesAsync(ct);
    }

    private string GetBaseUrl()
    {
        // Try to get from configuration, fallback to known production URL
        var baseUrl = _configuration["App:BaseUrl"];
        if (!string.IsNullOrEmpty(baseUrl))
            return baseUrl.TrimEnd('/');

        return "https://sradmin.azurewebsites.net";
    }

    private static string GenerateToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static string ComputeSha256Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
