using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using SportRental.Admin.Hubs;
using SportRental.Admin.Services.Sms;
using SportRental.Admin.Services.Email;

namespace SportRental.Admin.Services;

public interface IRentalConfirmationService
{
    /// <summary>
    /// Tworzy nowe potwierdzenie wynajmu i zwraca token do linku
    /// </summary>
    Task<string> CreateConfirmationAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>
    /// Wariant dla procesów bez ambient tenant context, np. webhooka Stripe.
    /// </summary>
    Task<string> CreateConfirmationForTenantAsync(Guid tenantId, Guid rentalId, CancellationToken ct = default);

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
    Task<ConfirmationDeliveryResult> SendConfirmationLinkAsync(Guid rentalId, string token, CancellationToken ct = default);

    /// <summary>
    /// Wariant dla procesów bez ambient tenant context, np. webhooka Stripe.
    /// </summary>
    Task<ConfirmationDeliveryResult> SendConfirmationLinkForTenantAsync(
        Guid tenantId, Guid rentalId, string token, CancellationToken ct = default);

    /// <summary>
    /// Ręczne potwierdzenie wynajmu przez pracownika z panelu admina
    /// </summary>
    Task<ConfirmationResult> ConfirmByAdminAsync(Guid rentalId, string adminUserName, CancellationToken ct = default);
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

public record ConfirmationDeliveryResult(
    bool SmsAttempted,
    bool SmsSent,
    bool EmailAttempted,
    bool EmailSent)
{
    public bool AnySent => SmsSent || EmailSent;
    public bool AllAttemptedSent =>
        (!SmsAttempted || SmsSent) && (!EmailAttempted || EmailSent);
}

public class RentalConfirmationService : IRentalConfirmationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<RentalConfirmationService> _logger;
    private readonly ISmsSender _smsSender;
    private readonly IRentalNotificationService _notificationService;
    private readonly IConfiguration _configuration;
    private readonly IEmailSender _emailSender;

    public RentalConfirmationService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ITenantProvider tenantProvider,
        ILogger<RentalConfirmationService> logger,
        ISmsSender smsSender,
        IRentalNotificationService notificationService,
        IConfiguration configuration,
        IEmailSender emailSender)
    {
        _contextFactory = contextFactory;
        _tenantProvider = tenantProvider;
        _logger = logger;
        _smsSender = smsSender;
        _notificationService = notificationService;
        _configuration = configuration;
        _emailSender = emailSender;
    }

    public Task<string> CreateConfirmationAsync(Guid rentalId, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        if (tenantId == null)
            throw new InvalidOperationException("No tenant context available");

        return CreateConfirmationForTenantAsync(tenantId.Value, rentalId, ct);
    }

    public Task<string> CreateConfirmationForTenantAsync(
        Guid tenantId, Guid rentalId, CancellationToken ct = default) =>
        CreateConfirmationCoreAsync(tenantId, rentalId, ct);

    private async Task<string> CreateConfirmationCoreAsync(
        Guid tenantId, Guid rentalId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.SetTenant(tenantId);

        var rental = await context.Rentals
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental?.Customer == null)
            throw new InvalidOperationException("Rental or customer not found");

        var now = DateTime.UtcNow;

        // Reużyj istniejący ważny token, zamiast generować nowy — edycja wynajmu nie
        // powinna unieważniać linku, który klient już dostał mailem.
        var existing = await context.RentalConfirmations
            .Where(rc => rc.RentalId == rentalId)
            .ToListAsync(ct);

        var reusable = existing
            .Where(rc => !rc.IsConfirmed && rc.ExpiresAt > now)
            .OrderByDescending(rc => rc.CreatedAt)
            .FirstOrDefault();

        if (reusable != null)
        {
            // Zaktualizuj email/telefon (mogły się zmienić przy edycji), wydłuż okno ważności.
            reusable.PhoneNumber = rental.Customer.PhoneNumber;
            reusable.Email = rental.Customer.Email;
            if (reusable.ExpiresAt < now.AddHours(24))
                reusable.ExpiresAt = now.AddHours(48);
            await context.SaveChangesAsync(ct);
            _logger.LogInformation("Reusing existing confirmation token for rental {RentalId}", rentalId);
            return reusable.Token;
        }

        // Usuń stare (wygasłe / potwierdzone) aby unique index na Token nie kolidował.
        if (existing.Any())
            context.RentalConfirmations.RemoveRange(existing);

        var token = GenerateToken();

        var confirmation = new RentalConfirmation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RentalId = rentalId,
            Token = token,
            PhoneNumber = rental.Customer.PhoneNumber,
            Email = rental.Customer.Email,
            CreatedAt = now,
            ExpiresAt = now.AddHours(48)
        };

        context.RentalConfirmations.Add(confirmation);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Created rental confirmation token for rental {RentalId}", rentalId);
        return token;
    }

    public async Task<ConfirmationPageData?> GetConfirmationDataAsync(string token, CancellationToken ct = default)
    {
        if (!IsValidTokenFormat(token))
        {
            _logger.LogWarning("Invalid confirmation token format (length={Length})", token?.Length ?? 0);
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var confirmation = await context.RentalConfirmations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rc => rc.Token == token, ct);

        if (confirmation == null)
        {
            _logger.LogWarning("Confirmation token not found (length={Length})", token?.Length ?? 0);
            return null;
        }

        context.SetTenant(confirmation.TenantId);

        // IgnoreQueryFilters — token już autoryzuje dostęp; bez tego ewentualna
        // rozbieżność tenanta (np. kliknięcie linku po zmianie sesji) daje null.
        var rental = await context.Rentals
            .IgnoreQueryFilters()
            .Include(r => r.Customer)
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(r => r.Id == confirmation.RentalId
                && r.TenantId == confirmation.TenantId, ct);

        if (rental == null)
        {
            _logger.LogWarning("Rental {RentalId} not found for confirmation {ConfirmationId}",
                confirmation.RentalId, confirmation.Id);
            return null;
        }

        if (rental.Customer == null)
        {
            _logger.LogWarning("Customer missing for rental {RentalId} (CustomerId={CustomerId})",
                rental.Id, rental.CustomerId);
            return null;
        }

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
            !string.IsNullOrWhiteSpace(rental.RegulationsTextSnapshot)
                ? rental.RegulationsTextSnapshot
                : company?.RegulationsText,
            confirmation.IsConfirmed,
            confirmation.ExpiresAt < DateTime.UtcNow
        );
    }

    public async Task<ConfirmationResult> ProcessConfirmationAsync(string token, string ip, string userAgent, CancellationToken ct = default)
    {
        if (!IsValidTokenFormat(token))
            return new ConfirmationResult(false, "Nieprawidłowy link potwierdzenia.");

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
            .IgnoreQueryFilters()
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == confirmation.RentalId
                && r.TenantId == confirmation.TenantId, ct);

        if (rental == null)
            return new ConfirmationResult(false, "Wynajem nie został znaleziony.");

        // Dla checkoutu online zapisujemy dowód dokładnie tej wersji, którą klient
        // zobaczył przed płatnością. Bieżący tekst firmy jest tylko fallbackiem dla
        // starszych i ręcznie tworzonych wynajmów bez snapshotu.
        var regulationsHash = rental.RegulationsHash;
        if (string.IsNullOrWhiteSpace(regulationsHash))
        {
            var company = await context.CompanyInfos.FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(company?.RegulationsText))
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

    public Task<ConfirmationDeliveryResult> SendConfirmationLinkAsync(
        Guid rentalId, string token, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        if (tenantId == null)
            throw new InvalidOperationException("No tenant context available");

        return SendConfirmationLinkForTenantAsync(tenantId.Value, rentalId, token, ct);
    }

    public Task<ConfirmationDeliveryResult> SendConfirmationLinkForTenantAsync(
        Guid tenantId, Guid rentalId, string token, CancellationToken ct = default) =>
        SendConfirmationLinkCoreAsync(tenantId, rentalId, token, ct);

    private async Task<ConfirmationDeliveryResult> SendConfirmationLinkCoreAsync(
        Guid tenantId, Guid rentalId, string token, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
        if (!IsValidTokenFormat(token))
            throw new ArgumentException("Confirmation token has an invalid format.", nameof(token));

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.SetTenant(tenantId);

        var rental = await context.Rentals
            .Include(r => r.Customer)
            .Include(r => r.MarketplaceOrder)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental?.Customer == null)
            throw new InvalidOperationException("Rental or customer not found");

        var baseUrl = GetBaseUrl();
        var confirmUrl = $"{baseUrl}/confirm/{token}";
        var rentalCode = rentalId.ToString()[..8].ToUpper();
        var customerName = rental.Customer.FullName ?? "Klient";
        var marketplaceOrderNumber = string.IsNullOrWhiteSpace(rental.MarketplaceOrder?.OrderNumber)
            ? null
            : rental.MarketplaceOrder.OrderNumber.Trim();

        var confirmation = await context.RentalConfirmations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rc => rc.Token == token
                && rc.TenantId == tenantId
                && rc.RentalId == rentalId, ct);

        if (confirmation == null)
            throw new InvalidOperationException("Confirmation token does not match the rental");

        // Webhook Stripe i redirect klienta mogą wejść równolegle w finalizację.
        // Flagi na confirmation czynią wysyłkę kanałów idempotentną przy retry.
        var smsAttempted = !confirmation.IsSmsSent &&
                           !string.IsNullOrWhiteSpace(rental.Customer.PhoneNumber);
        var emailAttempted = !confirmation.IsEmailSent &&
                             !string.IsNullOrWhiteSpace(rental.Customer.Email);
        var smsSent = confirmation.IsSmsSent;
        var emailSent = confirmation.IsEmailSent;

        // Send SMS
        if (smsAttempted)
        {
            try
            {
                var orderRecoveryHint = marketplaceOrderNumber == null
                    ? string.Empty
                    : $" Numer zamowienia potrzebny do odzyskania dostepu: {marketplaceOrderNumber}.";
                var smsMessage = $"Kliknij w link, aby potwierdzic wynajem {rentalCode}.{orderRecoveryHint} {confirmUrl}";
                await _smsSender.SendAsync(rental.Customer.PhoneNumber!, smsMessage, ct);
                confirmation.IsSmsSent = true;
                rental.IsSmsConfirmationSent = true;
                smsSent = true;
                _logger.LogInformation("Sent confirmation link SMS for rental {RentalId}", rentalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation link SMS for rental {RentalId}", rentalId);
            }
        }

        // Send Email with regulations + confirmation link
        if (emailAttempted)
        {
            try
            {
                var company = await context.CompanyInfos.FirstOrDefaultAsync(ct);
                var companyName = company?.Name ?? "SportRental";
                var regulationsText = !string.IsNullOrWhiteSpace(rental.RegulationsTextSnapshot)
                    ? rental.RegulationsTextSnapshot
                    : company?.RegulationsText;
                var encodedCompanyName = System.Net.WebUtility.HtmlEncode(companyName);
                var encodedCustomerName = System.Net.WebUtility.HtmlEncode(customerName);
                var marketplaceOrderSection = marketplaceOrderNumber == null
                    ? string.Empty
                    : $@"<div style='background:#eef6ff;border:1px solid #b8d8ff;border-radius:8px;padding:14px 16px;margin:16px 0;'>
  <b>Numer zamówienia:</b> {System.Net.WebUtility.HtmlEncode(marketplaceOrderNumber)}<br>
  <span style='font-size:13px;color:#445;'>Zachowaj ten numer — będzie potrzebny do odzyskania dostępu do zamówienia bez logowania.</span>
</div>";

                var items = await context.Set<RentalItem>()
                    .Where(ri => ri.RentalId == rentalId)
                    .Include(ri => ri.Product)
                    .ToListAsync(ct);

                var itemsHtml = string.Join("", items.Select(i =>
                    $"<tr><td style='padding:6px 12px;border:1px solid #ddd;'>{System.Net.WebUtility.HtmlEncode(i.Product?.Name ?? "Produkt")}</td>" +
                    $"<td style='padding:6px 12px;border:1px solid #ddd;text-align:center;'>{i.Quantity}</td>" +
                    $"<td style='padding:6px 12px;border:1px solid #ddd;text-align:right;'>{i.Subtotal:N2} zł</td></tr>"));

                var regulationsSection = !string.IsNullOrWhiteSpace(regulationsText)
                    ? $@"<h3 style='color:#333;margin-top:24px;'>📋 Regulamin wypożyczalni</h3>
                       <div style='background:#f8f9fa;border:1px solid #dee2e6;border-radius:8px;padding:16px;max-height:300px;overflow-y:auto;font-size:13px;white-space:pre-wrap;'>{System.Net.WebUtility.HtmlEncode(regulationsText)}</div>"
                    : "";

                var htmlBody = $@"<html><body style='font-family:Arial,sans-serif;color:#333;'>
<h2 style='color:#1976d2;'>Potwierdzenie wynajmu — {encodedCompanyName}</h2>
<p>Dzień dobry <b>{encodedCustomerName}</b>,</p>
<p>Umowę w formacie PDF wysłaliśmy w osobnej wiadomości. Aby sfinalizować rezerwację, prosimy o potwierdzenie wynajmu przyciskiem &quot;Potwierdź wynajem&quot; na dole tej wiadomości:</p>
{marketplaceOrderSection}

<h3 style='color:#333;'>📦 Szczegóły wynajmu</h3>
<table style='border-collapse:collapse;width:100%;max-width:500px;'>
<tr style='background:#1976d2;color:white;'>
  <th style='padding:8px 12px;text-align:left;'>Produkt</th>
  <th style='padding:8px 12px;text-align:center;'>Ilość</th>
  <th style='padding:8px 12px;text-align:right;'>Kwota</th>
</tr>
{itemsHtml}
<tr style='background:#f5f5f5;font-weight:bold;'>
  <td colspan='2' style='padding:8px 12px;border:1px solid #ddd;'>Razem</td>
  <td style='padding:8px 12px;border:1px solid #ddd;text-align:right;'>{rental.TotalAmount:N2} zł</td>
</tr>
</table>
<p><b>Okres:</b> {rental.StartDateUtc:dd.MM.yyyy} — {rental.EndDateUtc:dd.MM.yyyy}</p>
{(rental.DepositAmount > 0 ? $"<p><b>Kaucja:</b> {rental.DepositAmount:N2} zł</p>" : "")}

{regulationsSection}

<div style='margin-top:24px;'>
  <a href='{confirmUrl}' style='display:inline-block;background:#1976d2;color:white;padding:14px 32px;text-decoration:none;border-radius:8px;font-size:16px;font-weight:bold;'>✅ Potwierdź wynajem</a>
</div>
<p style='font-size:12px;color:#888;margin-top:16px;'>Link ważny 48 godzin. Klikając &quot;Potwierdź wynajem&quot; akceptujesz warunki regulaminu wypożyczalni.</p>
<p>Pozdrawiamy,<br>{encodedCompanyName}</p>
</body></html>";

                await _emailSender.SendEmailAsync(
                    rental.Customer.Email!,
                    $"Potwierdzenie wynajmu — {companyName}",
                    htmlBody);

                confirmation.IsEmailSent = true;
                emailSent = true;
                _logger.LogInformation("Sent confirmation email for rental {RentalId} to {Email}", rentalId, rental.Customer.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email for rental {RentalId}", rentalId);
            }
        }

        if (smsSent || emailSent)
            await context.SaveChangesAsync(ct);

        return new ConfirmationDeliveryResult(
            smsAttempted,
            smsSent,
            emailAttempted,
            emailSent);
    }

    public async Task<ConfirmationResult> ConfirmByAdminAsync(Guid rentalId, string adminUserName, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        if (tenantId == null)
            return new ConfirmationResult(false, "Brak kontekstu tenanta.");

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.SetTenant(tenantId);

        var rental = await context.Rentals
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental == null)
            return new ConfirmationResult(false, "Wynajem nie został znaleziony.");

        if (rental.IsSmsConfirmed)
            return new ConfirmationResult(true, "Wynajem został już wcześniej potwierdzony.");

        // Update rental
        rental.IsSmsConfirmed = true;
        if (rental.Status == RentalStatus.Pending)
            rental.Status = RentalStatus.Confirmed;

        // Update RentalConfirmation if exists
        var confirmation = await context.RentalConfirmations
            .FirstOrDefaultAsync(rc => rc.RentalId == rentalId, ct);

        if (confirmation != null)
        {
            confirmation.IsConfirmed = true;
            confirmation.ConfirmedAt = DateTime.UtcNow;
            confirmation.ConfirmedFromIp = "admin-panel";
            confirmation.ConfirmedUserAgent = $"Admin: {adminUserName}";
        }

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Rental {RentalId} manually confirmed by admin {Admin}", rentalId, adminUserName);

        // Notify via SignalR
        try
        {
            var notification = new RentalStatusChangedEvent(
                rental.Id,
                rental.Status.ToString(),
                rental.IsSmsConfirmed,
                rental.IsSmsConfirmationSent,
                DateTime.UtcNow);
            await _notificationService.NotifyRentalStatusChangedAsync(tenantId.Value, notification, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SignalR notification for rental {RentalId}", rentalId);
        }

        return new ConfirmationResult(true, "Wynajem został potwierdzony przez pracownika.");
    }

    private string GetBaseUrl()
    {
        // 1) Jawna konfiguracja (App:BaseUrl z Key Vault / App Service settings) ma pierwszeństwo.
        var baseUrl = _configuration["App:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl))
            return baseUrl.TrimEnd('/');

        // 2) Azure App Service ustawia WEBSITE_HOSTNAME na domyślny host bieżącego wdrożenia
        //    — dzięki temu linki potwierdzeń zawsze wskazują AKTUALNĄ appkę (bez hardkodowanego
        //    hosta infrastruktury w publicznym repo; wcześniejszy fallback wskazywał martwy host).
        var azureHost = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
        if (!string.IsNullOrWhiteSpace(azureHost))
            return $"https://{azureHost}";

        // 3) Dev fallback.
        return "http://localhost:5001";
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

    private static bool IsValidTokenFormat(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 32 or > 128)
            return false;

        foreach (var character in token)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
                return false;
        }

        return true;
    }

    private static string ComputeSha256Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
