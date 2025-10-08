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

        public SmsConfirmationService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ITenantProvider tenantProvider,
            ILogger<SmsConfirmationService> logger)
        {
            _contextFactory = contextFactory;
            _tenantProvider = tenantProvider;
            _logger = logger;
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
    }
}