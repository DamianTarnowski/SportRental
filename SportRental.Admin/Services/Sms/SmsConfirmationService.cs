using System.Security.Cryptography;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using SportRental.Admin.Hubs;
using Microsoft.EntityFrameworkCore;

#pragma warning disable CS0618 // Intentionally retained behind Sms:LegacyReplyConfirmationEnabled.

namespace SportRental.Admin.Services.Sms
{
    [Obsolete("Legacy SMS reply confirmation flow. Use RentalConfirmationService instead.")]
    public class SmsConfirmationService : ISmsConfirmationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ITenantProvider _tenantProvider;
        private readonly ILogger<SmsConfirmationService> _logger;
        private readonly ISmsSender _smsSender;
        private readonly IRentalNotificationService _notificationService;

        // Słowa kluczowe oznaczające potwierdzenie
        private static readonly string[] ConfirmationKeywords = { "TAK", "YES", "OK", "POTWIERDZAM", "ZGADZAM", "1" };
        private static readonly string[] RejectionKeywords = { "NIE", "NO", "REZYGNUJE", "ANULUJ", "0" };

        public SmsConfirmationService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ITenantProvider tenantProvider,
            ILogger<SmsConfirmationService> logger,
            ISmsSender smsSender,
            IRentalNotificationService notificationService)
        {
            _contextFactory = contextFactory;
            _tenantProvider = tenantProvider;
            _logger = logger;
            _smsSender = smsSender;
            _notificationService = notificationService;
        }

        public async Task<string> GenerateConfirmationCodeAsync(Guid rentalId, CancellationToken ct = default)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                throw new InvalidOperationException("No tenant context available");

            using var context = _contextFactory.CreateDbContext();
            context.SetTenant(tenantId);

            // SEC-011: kryptograficzny RNG zamiast Random.Shared (przewidywalny PRNG).
            // 6-cyfrowy kod (100000-999999), CSPRNG zgodny z ASVS L2 6.3.1.
            var plaintextCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            // Hash z Id jako salt (Id wygenerowany ponizej; dla atomic flow generujemy najpierw Id).
            var confirmationId = Guid.NewGuid();
            var codeHash = HashCode(confirmationId, plaintextCode);

            // Get rental to get phone number
            var rental = await context.Rentals
                .Include(r => r.Customer)
                .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

            if (rental?.Customer?.PhoneNumber == null)
                throw new InvalidOperationException("Rental or customer phone number not found");

            // Remove any existing confirmation for this rental
            var existing = await context.SmsConfirmations
                .Where(sc => sc.RentalId == rentalId)
                .ToListAsync(ct);

            if (existing.Any())
            {
                context.SmsConfirmations.RemoveRange(existing);
            }

            // SEC-012: zapisujemy HASH z Id-jako-salt, nigdy plaintext.
            var confirmation = new SmsConfirmation
            {
                Id = confirmationId,
                TenantId = tenantId.Value,
                RentalId = rentalId,
                Code = codeHash,
                PhoneNumber = NormalizePhoneNumber(rental.Customer.PhoneNumber),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            context.SmsConfirmations.Add(confirmation);
            await context.SaveChangesAsync(ct);

            _logger.LogInformation("Generated SMS confirmation code for rental {RentalId}", rentalId);

            // SMS wysyła sie z plaintext kodem do klienta — tylko DB ma hash.
            return plaintextCode;
        }

        public async Task<bool> ValidateConfirmationCodeAsync(Guid rentalId, string code, CancellationToken ct = default)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                return false;

            using var context = _contextFactory.CreateDbContext();
            context.SetTenant(tenantId);

            // SEC-012: szukamy po RentalId, porównujemy hash w pamięci constant-time
            // (zamiast WHERE Code = @code które ujawnia timing).
            var confirmation = await context.SmsConfirmations
                .Where(sc => sc.RentalId == rentalId)
                .OrderByDescending(sc => sc.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (confirmation == null)
            {
                _logger.LogWarning("No confirmation row for rental {RentalId}", rentalId);
                return false;
            }

            var expectedHash = HashCode(confirmation.Id, code);
            if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(expectedHash),
                System.Text.Encoding.ASCII.GetBytes(confirmation.Code)))
            {
                _logger.LogWarning("Invalid confirmation code for rental {RentalId}", rentalId);
                // Update attempts tracking even na zlym kodzie zeby zabezpieczyc brute-force
                confirmation.AttemptsCount++;
                confirmation.LastAttemptAt = DateTime.UtcNow;
                await context.SaveChangesAsync(ct);
                return false;
            }

            if (confirmation.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired confirmation code for rental {RentalId}", rentalId);
                return false;
            }

            if (confirmation.IsConfirmed)
            {
                _logger.LogWarning("Already confirmed code for rental {RentalId}", rentalId);
                return true; // Already confirmed, consider it valid
            }

            // Update attempt tracking
            confirmation.AttemptsCount++;
            confirmation.LastAttemptAt = DateTime.UtcNow;

            if (confirmation.AttemptsCount > 3)
            {
                _logger.LogWarning("Too many attempts for confirmation code for rental {RentalId}", rentalId);
                await context.SaveChangesAsync(ct);
                return false;
            }

            // Mark as confirmed
            confirmation.IsConfirmed = true;
            confirmation.ConfirmedAt = DateTime.UtcNow;

            await context.SaveChangesAsync(ct);

            _logger.LogInformation("Successfully validated confirmation code for rental {RentalId}", rentalId);

            return true;
        }

        public async Task MarkRentalAsConfirmedAsync(Guid rentalId, CancellationToken ct = default)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                throw new InvalidOperationException("No tenant context available");

            using var context = _contextFactory.CreateDbContext();
            context.SetTenant(tenantId);

            var rental = await context.Rentals
                .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

            if (rental == null)
                throw new InvalidOperationException("Rental not found");

            rental.IsSmsConfirmed = true;
            if (rental.Status == RentalStatus.Pending)
            {
                rental.Status = RentalStatus.Confirmed;
            }

            await context.SaveChangesAsync(ct);

            _logger.LogInformation("Marked rental {RentalId} as SMS confirmed", rentalId);
        }

        /// <summary>
        /// Przetwarza przychodzący SMS - szuka oczekującego potwierdzenia dla numeru telefonu
        /// </summary>
        public async Task<SmsProcessingResult> ProcessIncomingSmsAsync(string phoneNumber, string message, string? messageId = null, CancellationToken ct = default)
        {
            _logger.LogInformation("Processing incoming SMS from {PhoneNumber}: {Message}", phoneNumber, message);

            var normalizedPhone = NormalizePhoneNumber(phoneNumber);
            var normalizedMessage = message.Trim().ToUpperInvariant();

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            // Szukaj oczekującego potwierdzenia dla tego numeru telefonu (bez filtra tenanta)
            var pendingConfirmation = await context.SmsConfirmations
                .IgnoreQueryFilters()
                .Where(sc => sc.PhoneNumber == normalizedPhone && !sc.IsConfirmed && sc.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(sc => sc.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (pendingConfirmation == null)
            {
                _logger.LogInformation("No pending confirmation found for phone {PhoneNumber}", normalizedPhone);
                return new SmsProcessingResult(false, false, null, null);
            }

            // Sprawdź czy to potwierdzenie
            var isConfirmation = ConfirmationKeywords.Any(k => normalizedMessage.Contains(k));
            var isRejection = RejectionKeywords.Any(k => normalizedMessage.Contains(k));

            if (!isConfirmation && !isRejection)
            {
                _logger.LogInformation("Message does not contain confirmation or rejection keywords");
                return new SmsProcessingResult(true, false, pendingConfirmation.RentalId, 
                    "Nie rozpoznano odpowiedzi. Odpisz TAK aby potwierdzic lub NIE aby odrzucic.");
            }

            // Ustaw tenant dla operacji na rentalu
            context.SetTenant(pendingConfirmation.TenantId);

            var rental = await context.Rentals
                .Include(r => r.Customer)
                .FirstOrDefaultAsync(r => r.Id == pendingConfirmation.RentalId, ct);

            if (rental == null)
            {
                _logger.LogWarning("Rental {RentalId} not found for confirmation", pendingConfirmation.RentalId);
                return new SmsProcessingResult(false, false, null, null);
            }

            string responseMessage;

            if (isConfirmation)
            {
                // Potwierdź umowę
                pendingConfirmation.IsConfirmed = true;
                pendingConfirmation.ConfirmedAt = DateTime.UtcNow;
                
                rental.IsSmsConfirmed = true;
                if (rental.Status == RentalStatus.Pending)
                {
                    rental.Status = RentalStatus.Confirmed;
                }

                responseMessage = $"Dziekujemy! Umowa {pendingConfirmation.RentalId.ToString()[..8].ToUpper()} zostala potwierdzona. Do zobaczenia! - SportRental";
                _logger.LogInformation("Rental {RentalId} confirmed via SMS", rental.Id);
            }
            else
            {
                // Odrzucenie
                pendingConfirmation.IsConfirmed = false;
                pendingConfirmation.ConfirmedAt = DateTime.UtcNow;
                
                rental.Notes = (rental.Notes ?? "") + $"\n[SMS] Klient odrzucil warunki umowy: {DateTime.Now:dd.MM.yyyy HH:mm}";

                responseMessage = $"Umowa {pendingConfirmation.RentalId.ToString()[..8].ToUpper()} nie zostala potwierdzona. Skontaktuj sie z nami w razie pytan. - SportRental";
                _logger.LogInformation("Rental {RentalId} rejected via SMS", rental.Id);
            }

            await context.SaveChangesAsync(ct);

            // Powiadom UI o zmianie statusu przez SignalR
            try
            {
                var notification = new RentalStatusChangedEvent(
                    rental.Id,
                    rental.Status.ToString(),
                    rental.IsSmsConfirmed,
                    rental.IsSmsConfirmationSent,
                    DateTime.UtcNow);
                await _notificationService.NotifyRentalStatusChangedAsync(pendingConfirmation.TenantId, notification, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR notification for rental {RentalId}", rental.Id);
            }

            // Wyślij odpowiedź SMS
            try
            {
                await _smsSender.SendAsync(normalizedPhone, responseMessage, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation response SMS to {PhoneNumber}", normalizedPhone);
            }

            return new SmsProcessingResult(true, isConfirmation, rental.Id, responseMessage);
        }

        private static string NormalizePhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return phoneNumber;

            var cleaned = phoneNumber
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("(", "")
                .Replace(")", "");

            if (cleaned.StartsWith("+"))
                return cleaned;

            if (cleaned.StartsWith("48") && cleaned.Length > 9)
                return "+" + cleaned;

            if (cleaned.StartsWith("0"))
                return "+48" + cleaned[1..];

            return "+48" + cleaned;
        }

        /// <summary>
        /// SEC-012: SHA-256(salt || code), gdzie salt to GUID confirmation rekordu.
        /// Zwraca base64. Hash deterministyczny per-rekord (różne SmsConfirmation = różne hash dla tego samego kodu).
        /// </summary>
        private static string HashCode(Guid salt, string plaintextCode)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(salt.ToString("N") + ":" + plaintextCode);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}

#pragma warning restore CS0618
