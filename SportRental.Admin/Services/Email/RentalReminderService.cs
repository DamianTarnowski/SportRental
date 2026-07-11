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
        private readonly SemaphoreSlim _runLock = new(1, 1);
        private Timer? _timer;

        public RentalReminderService(IServiceScopeFactory scopeFactory, ILogger<RentalReminderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Primary reminder: 24h przed końcem dla Daily, 30 min dla Hourly.
        // Final reminder: 30 min przed końcem (email + SMS) — dla Daily jest to DODATKOWE
        // powiadomienie po primary, dla Hourly jest już pokryty przez primary (nie dublujemy).
        // Timer cadence musi być <= najkrótszego leada, żeby nie przegapić okna.
        private static readonly TimeSpan DailyReminderLead = TimeSpan.FromHours(24);
        private static readonly TimeSpan HourlyReminderLead = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan FinalReminderLead = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan OverdueReminder2Delay = TimeSpan.FromDays(1);
        private static readonly TimeSpan OverdueReminder3Delay = TimeSpan.FromDays(3);
        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Serwis przypomnień wynajmów został uruchomiony");
            _timer = new Timer(
                state => _ = CheckRentalsForReminders(state),
                null,
                TimeSpan.FromMinutes(1),
                TickInterval);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Serwis przypomnień wynajmów został zatrzymany");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private async Task CheckRentalsForReminders(object? state)
        {
            if (!await _runLock.WaitAsync(0))
            {
                _logger.LogWarning("Pomijam nakładający się przebieg serwisu przypomnień");
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                var smsSender = scope.ServiceProvider.GetRequiredService<ISmsSender>();

                await using var db = await dbFactory.CreateDbContextAsync();

                // Load company settings per tenant.
                // Demo tenanty POMIJAMY w całości: background nie ma ambient tenanta, więc
                // DemoAware* dekoratory nie złapią demo i wysłałyby REALNE maile/SMS-y
                // na fejkowe adresy/numery z seedu.
                var demoTenantIds = await db.Tenants.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(t => t.IsDemo)
                    .Select(t => t.Id)
                    .ToListAsync();
                var tenants = (await db.CompanyInfos.AsNoTracking().ToListAsync())
                    .Where(c => !demoTenantIds.Contains(c.TenantId))
                    .ToList();

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
            finally
            {
                _runLock.Release();
            }
        }

        private async Task ProcessTenantReminders(
            ApplicationDbContext db, CompanyInfo company,
            IEmailSender emailService, ISmsSender smsSender)
        {
            var currentTimeUtc = DateTime.UtcNow;
            var maxWindowUtc = currentTimeUtc.Add(DailyReminderLead);

            // Przetwarzamy również wynajmy po terminie, bo ustawienia zawierają osobne
            // przypomnienia na 1. i 3. dzień opóźnienia.
            var candidates = await db.Rentals
                .Include(r => r.Customer)
                .Include(r => r.Items)
                .Where(r => r.TenantId == company.TenantId
                         && r.Status == RentalStatus.Active
                         && r.IssuedAtUtc != null
                         && r.ReturnedAtUtc == null
                         && r.EndDateUtc <= maxWindowUtc)
                .ToListAsync();

            if (candidates.Count == 0) return;

            var productIds = candidates.SelectMany(r => r.Items.Select(i => i.ProductId)).Distinct().ToList();
            var products = await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            var rentalIds = candidates.Select(r => r.Id).ToList();
            var deliveries = (await db.RentalReminderDeliveries.AsNoTracking()
                    .Where(d => rentalIds.Contains(d.RentalId))
                    .Select(d => new { d.RentalId, d.Stage, d.Channel })
                    .ToListAsync())
                .Select(d => (d.RentalId, d.Stage, d.Channel))
                .ToHashSet();

            var emailCount = 0;
            var smsCount = 0;

            foreach (var rental in candidates)
            {
                var primaryLead = rental.RentalType == RentalType.Hourly ? HourlyReminderLead : DailyReminderLead;
                var primaryDueAtUtc = rental.EndDateUtc - primaryLead;
                var finalDueAtUtc = rental.EndDateUtc - FinalReminderLead;
                var primaryWindowOpen = currentTimeUtc >= primaryDueAtUtc
                    && currentTimeUtc < rental.EndDateUtc
                    && (rental.RentalType == RentalType.Hourly || currentTimeUtc < finalDueAtUtc);

                // Primary reminder: tylko przed terminem. Po terminie używamy dedykowanych
                // etapów. Dla wynajmu dziennego nie nadrabiamy go już w oknie finalnym,
                // bo klient dostałby dwa komunikaty jednocześnie.
                if (primaryWindowOpen
                    && !rental.IsReminderEmailSent
                    && !string.IsNullOrEmpty(rental.Customer?.Email))
                {
                    if (await SendReminderEmail(rental, company, products, emailService, db))
                        emailCount++;
                }

                // Final reminder (30 min przed końcem, email + SMS) — tylko dla Daily,
                // bo Hourly ma już primary = 30 min. Gdy primary = final nie dublujemy.
                if (rental.RentalType == RentalType.Daily
                    && currentTimeUtc >= finalDueAtUtc
                    && currentTimeUtc < rental.EndDateUtc
                    && !rental.IsFinalReminderSent)
                {
                    var emailApplicable = !string.IsNullOrEmpty(rental.Customer?.Email);
                    var smsApplicable = company.SmsReminderEnabled
                        && !string.IsNullOrWhiteSpace(rental.Customer?.PhoneNumber);

                    if (emailApplicable
                        && !HasDelivery(deliveries, rental.Id, RentalReminderStage.Final, RentalReminderChannel.Email)
                        && await SendFinalReminderEmail(rental, emailService))
                    {
                        await RecordDeliveryAsync(db, deliveries, rental, RentalReminderStage.Final, RentalReminderChannel.Email);
                        emailCount++;
                    }

                    if (smsApplicable
                        && !HasDelivery(deliveries, rental.Id, RentalReminderStage.Final, RentalReminderChannel.Sms)
                        && await SendFinalReminderSms(rental, company, products, smsSender))
                    {
                        await RecordDeliveryAsync(db, deliveries, rental, RentalReminderStage.Final, RentalReminderChannel.Sms);
                        smsCount++;
                    }

                    var allApplicableChannelsSent = (emailApplicable || smsApplicable)
                        && (!emailApplicable || HasDelivery(deliveries, rental.Id, RentalReminderStage.Final, RentalReminderChannel.Email))
                        && (!smsApplicable || HasDelivery(deliveries, rental.Id, RentalReminderStage.Final, RentalReminderChannel.Sms));
                    if (allApplicableChannelsSent)
                    {
                        rental.IsFinalReminderSent = true;
                        rental.FinalReminderSentAtUtc = DateTime.UtcNow;
                        db.Rentals.Update(rental);
                        await db.SaveChangesAsync();
                    }
                }

                if (company.SmsReminderEnabled
                    && primaryWindowOpen
                    && !rental.IsReminderSmsSent
                    && !string.IsNullOrWhiteSpace(rental.Customer?.PhoneNumber))
                {
                    if (await SendReminderSms(rental, company, products, smsSender, db))
                        smsCount++;
                }

                var overdue = currentTimeUtc - rental.EndDateUtc;
                if (overdue >= OverdueReminder3Delay)
                {
                    var sent = await TrySendConfiguredReminderAsync(
                        db, deliveries, rental, company, products, emailService, smsSender,
                        RentalReminderStage.OverdueDay3, company.EmailReminderText3,
                        company.SmsReminderText3);
                    emailCount += sent.EmailCount;
                    smsCount += sent.SmsCount;
                }
                else if (overdue >= OverdueReminder2Delay)
                {
                    var sent = await TrySendConfiguredReminderAsync(
                        db, deliveries, rental, company, products, emailService, smsSender,
                        RentalReminderStage.OverdueDay1, company.EmailReminderText2,
                        company.SmsReminderText2);
                    emailCount += sent.EmailCount;
                    smsCount += sent.SmsCount;
                }
            }

            if (emailCount > 0 || smsCount > 0)
            {
                _logger.LogInformation(
                    "Tenant {TenantId}: wysłano {EmailCount} email i {SmsCount} SMS przypomnień",
                    company.TenantId, emailCount, smsCount);
            }
        }

        private async Task<bool> SendFinalReminderSms(
            Rental rental, CompanyInfo company,
            Dictionary<Guid, string> products,
            ISmsSender smsSender)
        {
            if (rental.Customer == null) return false;

            try
            {
                // Bez polskich znakow — SMSAPI odrzuca niektore znaki spoza GSM (np. dlugi myslnik),
                // a diakrytyki potroilyby koszt (unicode). Konwencja jak w szablonach SmsApiSender.
                var minutes = Math.Max(1, (int)Math.Round((rental.EndDateUtc - DateTime.UtcNow).TotalMinutes));
                var equipment = string.Join(", ",
                    rental.Items.Select(i => products.GetValueOrDefault(i.ProductId, "sprzet")));
                if (string.IsNullOrEmpty(equipment)) equipment = "sprzet sportowy";
                var companyName = company.Name ?? "SportRental";
                var message = $"{companyName}: zostalo ok. {minutes} min do zwrotu sprzetu ({equipment}). Prosimy o przygotowanie do zwrotu lub kontakt ws. przedluzenia.";

                await smsSender.SendReminderAsync(
                    rental.Customer.PhoneNumber!,
                    rental.Customer.FullName,
                    message);

                _logger.LogInformation(
                    "Final SMS przypomnienie wysłane do {Phone} dla wynajmu {RentalId}",
                    rental.Customer.PhoneNumber, rental.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd final SMS przypomnienia do {Phone}", rental.Customer?.PhoneNumber);
                return false;
            }
        }

        private async Task<bool> SendReminderSms(
            Rental rental, CompanyInfo company,
            Dictionary<Guid, string> products,
            ISmsSender smsSender, ApplicationDbContext db)
        {
            if (rental.Customer == null) return false;

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
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd SMS przypomnienia do {Phone}", rental.Customer?.PhoneNumber);
                return false;
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

            return BuildConfiguredReminderText(template, rental, company, products);
        }

        private async Task<bool> SendReminderEmail(
            Rental rental,
            CompanyInfo company,
            Dictionary<Guid, string> products,
            IEmailSender emailService,
            ApplicationDbContext db)
        {
            if (rental.Customer?.Email == null) return false;

            try
            {
                var remaining = rental.EndDateUtc - DateTime.UtcNow;
                var timePhrase = FormatRemaining(remaining);
                var endLocal = PolishTimeZone.FromUtc(rental.EndDateUtc);
                var defaultText = $@"
                    Przypominamy, że Twój wynajem sprzętu sportowego kończy się {timePhrase} (dnia {endLocal:yyyy-MM-dd} o {endLocal:HH:mm}).

                    Prosimy o terminowy zwrot wypożyczonego sprzętu.

                    W razie pytań prosimy o kontakt.";
                var reminderText = string.IsNullOrWhiteSpace(company.EmailReminderText)
                    ? defaultText
                    : BuildConfiguredReminderText(company.EmailReminderText, rental, company, products);

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
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas wysyłania przypomnienia do {Email}", rental.Customer?.Email);
                return false;
            }
        }

        private async Task<bool> SendFinalReminderEmail(Rental rental, IEmailSender emailService)
        {
            if (rental.Customer?.Email == null) return false;

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

                _logger.LogInformation("Final reminder wysłany do {Email} dla wynajmu {RentalId}",
                    rental.Customer.Email, rental.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas wysyłania final reminder do {Email}", rental.Customer?.Email);
                return false;
            }
        }

        private async Task<(int EmailCount, int SmsCount)> TrySendConfiguredReminderAsync(
            ApplicationDbContext db,
            HashSet<(Guid RentalId, RentalReminderStage Stage, RentalReminderChannel Channel)> deliveries,
            Rental rental,
            CompanyInfo company,
            Dictionary<Guid, string> products,
            IEmailSender emailService,
            ISmsSender smsSender,
            RentalReminderStage stage,
            string? emailTemplate,
            string? smsTemplate)
        {
            if (rental.Customer == null) return (0, 0);

            var emailCount = 0;
            var smsCount = 0;

            if (!string.IsNullOrWhiteSpace(emailTemplate)
                && !string.IsNullOrWhiteSpace(rental.Customer.Email)
                && !HasDelivery(deliveries, rental.Id, stage, RentalReminderChannel.Email))
            {
                try
                {
                    var text = BuildConfiguredReminderText(emailTemplate, rental, company, products);
                    await emailService.SendReminderAsync(
                        rental.Customer.Email,
                        rental.Customer.FullName,
                        text);
                    await RecordDeliveryAsync(db, deliveries, rental, stage, RentalReminderChannel.Email);
                    emailCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Błąd email przypomnienia {Stage} dla wynajmu {RentalId}", stage, rental.Id);
                }
            }

            if (company.SmsReminderEnabled
                && !string.IsNullOrWhiteSpace(smsTemplate)
                && !string.IsNullOrWhiteSpace(rental.Customer.PhoneNumber)
                && !HasDelivery(deliveries, rental.Id, stage, RentalReminderChannel.Sms))
            {
                try
                {
                    var text = BuildConfiguredReminderText(smsTemplate, rental, company, products);
                    await smsSender.SendReminderAsync(
                        rental.Customer.PhoneNumber,
                        rental.Customer.FullName,
                        text);
                    await RecordDeliveryAsync(db, deliveries, rental, stage, RentalReminderChannel.Sms);
                    smsCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Błąd SMS przypomnienia {Stage} dla wynajmu {RentalId}", stage, rental.Id);
                }
            }

            return (emailCount, smsCount);
        }

        private static string BuildConfiguredReminderText(
            string template,
            Rental rental,
            CompanyInfo company,
            Dictionary<Guid, string> products)
        {
            var customerName = rental.Customer?.FullName ?? "Kliencie";
            var endDate = PolishTimeZone.FromUtc(rental.EndDateUtc).ToString("dd.MM.yyyy HH:mm");
            var equipmentList = string.Join(", ",
                rental.Items.Select(i => products.GetValueOrDefault(i.ProductId, "sprzet")));
            if (string.IsNullOrEmpty(equipmentList)) equipmentList = "sprzet sportowy";

            return template
                .Replace("{imie}", customerName)
                .Replace("{data_konca}", endDate)
                .Replace("{sprzet}", equipmentList)
                .Replace("{firma}", company.Name ?? "SportRental")
                .Replace("{telefon}", company.PhoneNumber ?? "");
        }

        private static bool HasDelivery(
            HashSet<(Guid RentalId, RentalReminderStage Stage, RentalReminderChannel Channel)> deliveries,
            Guid rentalId,
            RentalReminderStage stage,
            RentalReminderChannel channel) =>
            deliveries.Contains((rentalId, stage, channel));

        private static async Task RecordDeliveryAsync(
            ApplicationDbContext db,
            HashSet<(Guid RentalId, RentalReminderStage Stage, RentalReminderChannel Channel)> deliveries,
            Rental rental,
            RentalReminderStage stage,
            RentalReminderChannel channel)
        {
            if (!deliveries.Add((rental.Id, stage, channel))) return;

            db.RentalReminderDeliveries.Add(new RentalReminderDelivery
            {
                Id = Guid.NewGuid(),
                TenantId = rental.TenantId,
                RentalId = rental.Id,
                Stage = stage,
                Channel = channel,
                SentAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
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
            _runLock.Dispose();
        }
    }
}
