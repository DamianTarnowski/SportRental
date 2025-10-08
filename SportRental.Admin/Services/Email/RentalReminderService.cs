using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace SportRental.Admin.Services.Email
{
    public class RentalReminderService : IHostedService, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RentalReminderService> _logger;
        private Timer? _timer;

        public RentalReminderService(IServiceScopeFactory scopeFactory, ILogger<RentalReminderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Serwis przypomnień wynajmów został uruchomiony");
            
            // Uruchom co godzinę (3600000 ms)
            _timer = new Timer(CheckRentalsForReminders, null, TimeSpan.Zero, TimeSpan.FromHours(1));
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Serwis przypomnień wynajmów został zatrzymany");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private async void CheckRentalsForReminders(object? state)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailSender>();

                await using var db = await dbFactory.CreateDbContextAsync();

                var currentTimeUtc = DateTime.UtcNow;
                var reminderTimeUtc = currentTimeUtc.AddHours(24); // Przypomnij 24h przed końcem

                // Znajdź aktywne wynajmy które kończą się w ciągu najbliższych 24h
                var rentalsToRemind = await db.Rentals
                    .Include(r => r.Customer)
                    .Where(r => r.Status == RentalStatus.Active 
                             && r.EndDateUtc <= reminderTimeUtc 
                             && r.EndDateUtc > currentTimeUtc
                             && !string.IsNullOrEmpty(r.Customer!.Email))
                    .ToListAsync();

                foreach (var rental in rentalsToRemind)
                {
                    await SendReminderEmail(rental, emailService);
                }

                if (rentalsToRemind.Any())
                {
                    _logger.LogInformation("Wysłano {Count} przypomnień o zakończeniu wynajmu", rentalsToRemind.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas sprawdzania przypomnień wynajmów");
            }
        }

        private async Task SendReminderEmail(Rental rental, IEmailSender emailService)
        {
            if (rental.Customer?.Email == null) return;

            try
            {
                var hoursUntilEnd = (rental.EndDateUtc - DateTime.UtcNow).TotalHours;
                var reminderText = $@"
                    Przypominamy, że Twój wynajem sprzętu sportowego kończy się za {hoursUntilEnd:F0} godzin 
                    (dnia {rental.EndDateUtc.ToLocalTime():yyyy-MM-dd o HH:mm}).
                    
                    Prosimy o terminowy zwrot wypożyczonego sprzętu.
                    
                    W razie pytań prosimy o kontakt.";

                await emailService.SendReminderAsync(
                    rental.Customer.Email, 
                    rental.Customer.FullName, 
                    reminderText);

                _logger.LogInformation("Przypomnienie wysłane do {Email} dla wynajmu {RentalId}", 
                    rental.Customer.Email, rental.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas wysyłania przypomnienia do {Email}", rental.Customer?.Email);
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}