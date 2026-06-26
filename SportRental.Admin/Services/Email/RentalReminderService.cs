using SportRental.Admin.Services.Sms;
using SportRental.Admin.Services.Time;
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

        // Primary reminder: 24h przed końcem dla Daily, 15 min dla Hourly.
        // Final reminder: zawsze 15 min przed końcem — dla Daily jest to DODATKOWY mail po primary,
        // dla Hourly jest już pokryty przez primary (nie wysyłamy drugiego).
        // Timer cadence musi być <= najkrótszego leada, żeby nie przegapić okna.
        private static readonly TimeSpan DailyReminderLead = TimeSpan.FromHours(24);
        private static readonly TimeSpan HourlyReminderLead = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan FinalReminderLead = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Serwis przypomnień wynajmów został uruchomiony");
            _timer = new Timer(CheckRentalsForReminders, null, TimeSpan.FromMinutes(1), TickInterval);
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
                var smsSender = scope.ServiceProvider.GetRequiredService<ISmsSender>();

                await using var db = await dbFactory.CreateDbContextAsync();

                // Load company settings per tenant
                var tenants = await db.CompanyInfos.AsNoTracking().ToListAsync();

                foreach (var company in tenants)
                {
                    try
                    {
                        await ProcessTenantReminders(db, company, emailService, smsSender);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Błąd przypomnień dla tenanta {TenantId}", company.TenantId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas sprawdzania przypomnień wynajmów");
            }
        }

        private async Task ProcessTenantReminders(
            ApplicationDbContext db, CompanyInfo company,
            IEmailSender emailService, ISmsSender smsSender)
        {
            var currentTimeUtc = DateTime.UtcNow;
            // Prefilter: anything that might be due in the next DailyReminderLead window and still active.
            // Per-rental decision (hourly vs daily lead) is made below.
            var maxWindowUtc = currentTimeUtc.Add(DailyReminderLead);

            // NXRE r2 audit: reminder o zwrocie tylko dla wynajmów WYDANYCH — jeśli sprzęt
            // jeszcze nie odebrany, klient nie powinien dostawać "przypomnienia o zwrocie".
            // (Confirmed niewydane są wykluczone przez IssuedAtUtc != null).
            var candidates = await db.Rentals
                .Include(r => r.Customer)
                .Include(r => r.Items)
                .Where(r => r.TenantId == company.TenantId
                         && r.Status == RentalStatus.Active
                         && r.IssuedAtUtc != null
                         && r.ReturnedAtUtc == null
                         && r.EndDateUtc > currentTimeUtc
                         && r.EndDateUtc <= maxWindowUtc
                         && (!r.IsReminderEmailSent || !r.IsReminderSmsSent || !r.IsFinalReminderSent))
                .ToListAsync();

            if (candidates.Count == 0) return;

            var productIds = candidates.SelectMany(r => r.Items.Select(i => i.ProductId)).Distinct().ToList();
            var products = await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            var emailCount = 0;
            var finalEmailCount = 0;
            var smsCount = 0;

            foreach (var rental in candidates)
            {
                var primaryLead = rental.RentalType == RentalType.Hourly ? HourlyReminderLead : DailyReminderLead;
                var primaryDueAtUtc = rental.EndDateUtc - primaryLead;
                var finalDueAtUtc = rental.EndDateUtc - FinalReminderLead;

                // Primary reminder (24h dla Daily, 15 min dla Hourly).
                if (currentTimeUtc >= primaryDueAtUtc
                    && !rental.IsReminderEmailSent
                    && !string.IsNullOrEmpty(rental.Customer?.Email))
                {
                    await SendReminderEmail(rental, emailService, db);
                    emailCount++;
                }

                // Final reminder (15 min przed końcem) — tylko dla Daily, bo Hourly
                // ma już primary = 15min. Gdy primary = final nie wysyłamy drugiej kopii.
                if (rental.RentalType == RentalType.Daily
                    && currentTimeUtc >= finalDueAtUtc
                    && !rental.IsFinalReminderSent
                    && !string.IsNullOrEmpty(rental.Customer?.Email))
                {
                    await SendFinalReminderEmail(rental, emailService, db);
                    finalEmailCount++;
                }

                if (company.SmsReminderEnabled
                    && currentTimeUtc >= primaryDueAtUtc
                    && !rental.IsReminderSmsSent
                    && !string.IsNullOrWhiteSpace(rental.Customer?.PhoneNumber))
                {
                    await SendReminderSms(rental, company, products, smsSender, db);
                    smsCount++;
                }
            }

            if (emailCount > 0 || finalEmailCount > 0 || smsCount > 0)
            {
                _logger.LogInformation(
                    "Tenant {TenantId}: wysłano {EmailCount} email, {FinalEmailCount} final email, {SmsCount} SMS przypomnień",
                    company.TenantId, emailCount, finalEmailCount, smsCount);
            }
        }

        private async Task SendReminderSms(
            Rental rental, CompanyInfo company,
            Dictionary<Guid, string> products,
            ISmsSender smsSender, ApplicationDbContext db)
        {
            if (rental.Customer == null) return;

            try
            {
                var message = BuildReminderSmsText(rental, company, products);

                await smsSender.SendReminderAsync(
                    rental.Customer.PhoneNumber!,
                    rental.Customer.FullName,
                    message);

                rental.IsReminderSmsSent = true;
                rental.ReminderSmsSentAtUtc = DateTime.UtcNow;
                db.Rentals.Update(rental);
                await db.SaveChangesAsync();

                _logger.LogInformation(
                    "SMS przypomnienie wysłane do {Phone} dla wynajmu {RentalId}",
                    rental.Customer.PhoneNumber, rental.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd SMS przypomnienia do {Phone}", rental.Customer?.PhoneNumber);
            }
        }

        public static string BuildReminderSmsText(
            Rental rental, CompanyInfo company, Dictionary<Guid, string> products)
        {
            var template = company.SmsReminderText;

            if (string.IsNullOrWhiteSpace(template))
            {
                template = "Witaj {imie}! Wynajem sprzetu ({sprzet}) konczy sie {data_konca}. Prosimy o zwrot lub kontakt ws. przedluzenia. {firma}";
            }

            var customerName = rental.Customer?.FullName ?? "Kliencie";
            var endDate = PolishTimeZone.FromUtc(rental.EndDateUtc).ToString("dd.MM.yyyy HH:mm");
            var equipmentList = string.Join(", ",
                rental.Items.Select(i => products.GetValueOrDefault(i.ProductId, "sprzet")));
            if (string.IsNullOrEmpty(equipmentList)) equipmentList = "sprzet sportowy";
            var companyName = company.Name ?? "SportRental";
            var companyPhone = company.PhoneNumber ?? "";

            return template
                .Replace("{imie}", customerName)
                .Replace("{data_konca}", endDate)
                .Replace("{sprzet}", equipmentList)
                .Replace("{firma}", companyName)
                .Replace("{telefon}", companyPhone);
        }

        private async Task SendReminderEmail(Rental rental, IEmailSender emailService, ApplicationDbContext db)
        {
            if (rental.Customer?.Email == null) return;

            try
            {
                var remaining = rental.EndDateUtc - DateTime.UtcNow;
                var timePhrase = FormatRemaining(remaining);
                var endLocal = PolishTimeZone.FromUtc(rental.EndDateUtc);
                var reminderText = $@"
                    Przypominamy, że Twój wynajem sprzętu sportowego kończy się {timePhrase} (dnia {endLocal:yyyy-MM-dd} o {endLocal:HH:mm}).

                    Prosimy o terminowy zwrot wypożyczonego sprzętu.

                    W razie pytań prosimy o kontakt.";

                await emailService.SendReminderAsync(
                    rental.Customer.Email,
                    rental.Customer.FullName,
                    reminderText);

                rental.IsReminderEmailSent = true;
                rental.ReminderEmailSentAtUtc = DateTime.UtcNow;
                db.Rentals.Update(rental);
                await db.SaveChangesAsync();

                _logger.LogInformation("Przypomnienie email wysłane do {Email} dla wynajmu {RentalId}",
                    rental.Customer.Email, rental.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas wysyłania przypomnienia do {Email}", rental.Customer?.Email);
            }
        }

        private async Task SendFinalReminderEmail(Rental rental, IEmailSender emailService, ApplicationDbContext db)
        {
            if (rental.Customer?.Email == null) return;

            try
            {
                var remaining = rental.EndDateUtc - DateTime.UtcNow;
                var timePhrase = FormatRemaining(remaining);
                var endLocal = PolishTimeZone.FromUtc(rental.EndDateUtc);
                var reminderText = $@"
                    Ostatnie przypomnienie: Twój wynajem sprzętu sportowego kończy się {timePhrase} (o godz. {endLocal:HH:mm}).

                    Prosimy o przygotowanie sprzętu do zwrotu lub kontakt w sprawie przedłużenia.";

                await emailService.SendReminderAsync(
                    rental.Customer.Email,
                    rental.Customer.FullName,
                    reminderText);

                rental.IsFinalReminderSent = true;
                rental.FinalReminderSentAtUtc = DateTime.UtcNow;
                db.Rentals.Update(rental);
                await db.SaveChangesAsync();

                _logger.LogInformation("Final reminder wysłany do {Email} dla wynajmu {RentalId}",
                    rental.Customer.Email, rental.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas wysyłania final reminder do {Email}", rental.Customer?.Email);
            }
        }

        // Polska fleksja + sensowna jednostka zależnie od skali pozostałego czasu.
        private static string FormatRemaining(TimeSpan remaining)
        {
            if (remaining.TotalMinutes <= 0) return "lada chwila";
            if (remaining.TotalHours < 1)
            {
                var m = Math.Max(1, (int)Math.Round(remaining.TotalMinutes));
                return $"za {m} {PluralPl(m, "minutę", "minuty", "minut")}";
            }
            var h = (int)Math.Round(remaining.TotalHours);
            return $"za {h} {PluralPl(h, "godzinę", "godziny", "godzin")}";
        }

        private static string PluralPl(int n, string one, string few, string many)
        {
            var mod10 = n % 10;
            var mod100 = n % 100;
            if (n == 1) return one;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return few;
            return many;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}