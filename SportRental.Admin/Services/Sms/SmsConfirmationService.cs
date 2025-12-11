using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace SportRental.Admin.Services.Sms
{
    public class SmsConfirmationService : ISmsConfirmationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ITenantProvider _tenantProvider;
        private readonly ILogger<SmsConfirmationService> _logger;
        private readonly ISmsSender _smsSender;

        // Słowa kluczowe oznaczające potwierdzenie
        private static readonly string[] ConfirmationKeywords = { "TAK", "YES", "OK", "POTWIERDZAM", "ZGADZAM", "1" };
        private static readonly string[] RejectionKeywords = { "NIE", "NO", "REZYGNUJE", "ANULUJ", "0" };

        public SmsConfirmationService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ITenantProvider tenantProvider,
            ILogger<SmsConfirmationService> logger,
            ISmsSender smsSender)
        {
            _contextFactory = contextFactory;
            _tenantProvider = tenantProvider;
            _logger = logger;
            _smsSender = smsSender;
        }

        public async Task<string> GenerateConfirmationCodeAsync(Guid rentalId, CancellationToken ct = default)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                throw new InvalidOperationException("No tenant context available");

            using var context = _contextFactory.CreateDbContext();
            context.SetTenant(tenantId);

            // Generate a 6-digit code
            var code = Random.Shared.Next(100000, 999999).ToString();

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

            // Create new confirmation
            var confirmation = new SmsConfirmation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.Value,
                RentalId = rentalId,
                Code = code,
                PhoneNumber = rental.Customer.PhoneNumber,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            context.SmsConfirmations.Add(confirmation);
            await context.SaveChangesAsync(ct);

            _logger.LogInformation("Generated SMS confirmation code for rental {RentalId}", rentalId);

            return code;
        }

        public async Task<bool> ValidateConfirmationCodeAsync(Guid rentalId, string code, CancellationToken ct = default)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                return false;

            using var context = _contextFactory.CreateDbContext();
            context.SetTenant(tenantId);

            var confirmation = await context.SmsConfirmations
                .FirstOrDefaultAsync(sc => sc.RentalId == rentalId && sc.Code == code, ct);

            if (confirmation == null)
            {
                _logger.LogWarning("Invalid confirmation code for rental {RentalId}", rentalId);
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
    }
}