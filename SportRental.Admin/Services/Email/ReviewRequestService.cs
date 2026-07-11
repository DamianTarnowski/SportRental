using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Email
{
    /// <summary>
    /// Po zakończeniu wynajmu (Status=Completed) wysyła do klienta maksymalnie trzy
    /// prośby o wystawienie opinii: +24h, +3d, +7d od zakończenia.
    /// Przestaje wysyłać jeśli:
    ///   - klient zostawił już opinię (RentalReview dla RentalId),
    ///   - klient zrezygnował (Customer.ReviewEmailsOptOut = true),
    ///   - wysłano już 3 prośby.
    /// </summary>
    public class ReviewRequestService : IHostedService, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReviewRequestService> _logger;
        private Timer? _timer;

        public ReviewRequestService(IServiceScopeFactory scopeFactory, ILogger<ReviewRequestService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Odstępy od zakończenia wynajmu — kolejne próby.
        private static readonly TimeSpan[] Offsets =
        {
            TimeSpan.FromHours(24),
            TimeSpan.FromDays(3),
            TimeSpan.FromDays(7)
        };

        // Jak długo po ostatniej próbie (cooldown) czekamy przed kolejną — zapobiega
        // wysłaniu dwóch przypomnień obok siebie, gdy serwis wystartuje z opóźnieniem.
        private static readonly TimeSpan MinIntervalBetweenRequests = TimeSpan.FromHours(12);

        // Tick co godzinę — nie ma sensu częściej; progi są liczone w godzinach/dniach.
        private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Serwis próśb o opinie został uruchomiony");
            _timer = new Timer(Tick, null, TimeSpan.FromMinutes(2), TickInterval);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Serwis próśb o opinie został zatrzymany");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private async void Tick(object? state)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                var protectorProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var surveyTokenService = scope.ServiceProvider.GetRequiredService<IReviewSurveyTokenService>();

                // Admin URL — dla survey linku; ClientApp URL — dla opt-out. Produkcyjny
                // fallback wskazuje na WASM bundlowany w Admin pod /_client.
                var adminBaseUrl = config["Admin:PublicBaseUrl"]?.TrimEnd('/') ?? string.Empty;
                var clientBaseUrl = ClientAppUrlResolver.Resolve(config, adminBaseUrl);

                await using var db = await dbFactory.CreateDbContextAsync();
                await ProcessAsync(db, emailSender, protectorProvider, surveyTokenService, adminBaseUrl, clientBaseUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd w serwisie próśb o opinie");
            }
        }

        private async Task ProcessAsync(
            ApplicationDbContext db,
            IEmailSender emailSender,
            IDataProtectionProvider protectorProvider,
            IReviewSurveyTokenService surveyTokenService,
            string adminBaseUrl,
            string clientBaseUrl)
        {
            var now = DateTime.UtcNow;
            var earliest = now - Offsets.Last() - TimeSpan.FromDays(1);

            // Kandydaci: zakończone wynajmy w ostatnich ~8 dniach, klient z e-mailem i bez opt-out,
            // jeszcze nie wysłano 3 próśb, jeszcze nie ma opinii.
            var rentals = await db.Rentals.IgnoreQueryFilters()
                .Include(r => r.Customer)
                .Where(r => r.Status == RentalStatus.Completed
                         && r.ReturnedAtUtc != null
                         && r.ReturnedAtUtc >= earliest
                         && r.ReviewRequestCount < Offsets.Length
                         && r.Customer != null
                         && r.Customer.Email != null
                         && !r.Customer.ReviewEmailsOptOut)
                .ToListAsync();

            if (rentals.Count == 0) return;

            var rentalIds = rentals.Select(r => r.Id).ToList();
            var reviewed = await db.RentalReviews.IgnoreQueryFilters()
                .Where(rv => rentalIds.Contains(rv.RentalId))
                .Select(rv => rv.RentalId)
                .ToListAsync();
            var reviewedSet = reviewed.ToHashSet();

            var protector = protectorProvider.CreateProtector("ReviewOptOut");
            var sent = 0;

            foreach (var rental in rentals)
            {
                if (reviewedSet.Contains(rental.Id)) continue;

                // Sprawdź czy kolejna próba jest już należna.
                var returnedAt = rental.ReturnedAtUtc!.Value;
                var nextIdx = rental.ReviewRequestCount;
                if (nextIdx >= Offsets.Length) continue;

                var dueAt = returnedAt + Offsets[nextIdx];
                if (now < dueAt) continue;

                if (rental.LastReviewRequestSentAtUtc is DateTime last && now - last < MinIntervalBetweenRequests)
                {
                    continue;
                }

                try
                {
                    var token = protector.Protect(rental.Customer!.Id.ToString("N"));
                    var optOutUrl = string.IsNullOrEmpty(clientBaseUrl)
                        ? string.Empty
                        : $"{clientBaseUrl}/reviews/opt-out?token={Uri.EscapeDataString(token)}";

                    var surveyToken = surveyTokenService.Generate(rental.Id);
                    var surveyUrl = string.IsNullOrEmpty(adminBaseUrl)
                        ? string.Empty
                        : $"{adminBaseUrl}/ankieta/{rental.Id:D}?t={Uri.EscapeDataString(surveyToken)}";

                    var body = BuildBody(rental, surveyUrl, optOutUrl);
                    await emailSender.SendEmailAsync(
                        rental.Customer.Email!,
                        "Podziel się opinią o wypożyczeniu",
                        body);

                    rental.ReviewRequestCount++;
                    rental.LastReviewRequestSentAtUtc = now;
                    db.Rentals.Update(rental);
                    await db.SaveChangesAsync();

                    sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd podczas wysyłki prośby o opinię dla wynajmu {RentalId}", rental.Id);
                }
            }

            if (sent > 0)
            {
                _logger.LogInformation("Wysłano {Count} prośby o opinie", sent);
            }
        }

        private static string BuildBody(Rental rental, string surveyUrl, string optOutUrl)
        {
            var ctaBlock = string.IsNullOrEmpty(surveyUrl)
                ? string.Empty
                : $"<p><a href=\"{surveyUrl}\" style=\"display:inline-block;padding:10px 18px;background:#2e7d32;color:#fff;text-decoration:none;border-radius:6px;\">Wystaw opinię</a></p>";

            var optOutBlock = string.IsNullOrEmpty(optOutUrl)
                ? string.Empty
                : $"<p style=\"font-size:12px;color:#777;\">Jeśli nie chcesz otrzymywać takich wiadomości, <a href=\"{optOutUrl}\">zrezygnuj</a>.</p>";

            return $@"
                <p>Dzień dobry,</p>
                <p>dziękujemy za skorzystanie z naszego wypożyczenia. Twoja opinia pomaga nam i innym klientom.</p>
                {ctaBlock}
                <p>Pozdrawiamy,<br/>Zespół SportRental</p>
                {optOutBlock}";
        }

        public void Dispose() => _timer?.Dispose();
    }
}
