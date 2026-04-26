using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services
{
    /// <summary>
    /// Oblicza poziom zaufania klienta (TrustLevel) z agregatu CustomerReview ze WSZYSTKICH
    /// wypożyczalni (cross-tenant). Wywoływany po insercie/usunięciu CustomerReview.
    ///
    /// Reguły:
    ///   Restricted — admin manual block, średnia &lt; 5.0, lub 3+ incydentów (score &lt; 5
    ///                w którymś z 3 wymiarów)
    ///   Watch      — średnia 5.0-8.0 lub 1-2 incydenty
    ///   Good       — 10+ ocen, średnia ≥ 8.0, brak incydentów
    ///   Unverified — domyślnie nowy klient (mniej niż 10 ocen)
    /// </summary>
    public interface ICustomerTrustCalculator
    {
        Task RecalculateAsync(Guid customerId, CancellationToken ct = default);
    }

    public class CustomerTrustCalculator : ICustomerTrustCalculator
    {
        private const int MinReviewsForGood = 10;
        private const double GoodScoreThreshold = 8.0;
        private const double RestrictedScoreThreshold = 5.0;
        private const int RestrictedIncidentThreshold = 3;

        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        public CustomerTrustCalculator(IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task RecalculateAsync(Guid customerId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Wszystkie recenzje klienta z każdej wypożyczalni — IgnoreQueryFilters dla cross-tenant.
            var reviews = await db.CustomerReviews.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(cr => cr.CustomerId == customerId)
                .Select(cr => new
                {
                    cr.TimelinessScore,
                    cr.ConditionScore,
                    cr.CommunicationScore
                })
                .ToListAsync(ct);

            var customer = await db.Customers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == customerId, ct);
            if (customer is null) return;

            var count = reviews.Count;
            var sum = reviews.Sum(r => r.TimelinessScore + r.ConditionScore + r.CommunicationScore);
            var avg = count == 0 ? 0.0 : (double)sum / (count * 3);
            var incidents = reviews.Count(r =>
                r.TimelinessScore < 5 || r.ConditionScore < 5 || r.CommunicationScore < 5);

            customer.TrustCompletedRentalsCount = count;
            customer.TrustAverageScore = Math.Round(avg, 2);
            customer.TrustIncidentCount = incidents;
            customer.TrustLevelCalculatedAtUtc = DateTime.UtcNow;
            customer.TrustLevel = customer.TrustLevelManualOverride
                ?? ComputeLevel(count, avg, incidents);

            await db.SaveChangesAsync(ct);
        }

        private static CustomerTrustLevel ComputeLevel(int count, double avg, int incidents)
        {
            if (avg < RestrictedScoreThreshold && count > 0) return CustomerTrustLevel.Restricted;
            if (incidents >= RestrictedIncidentThreshold) return CustomerTrustLevel.Restricted;

            if (count < MinReviewsForGood) return CustomerTrustLevel.Unverified;

            if (avg >= GoodScoreThreshold && incidents == 0) return CustomerTrustLevel.Good;

            return CustomerTrustLevel.Watch;
        }
    }
}
