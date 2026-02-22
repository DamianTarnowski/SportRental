using SportRental.Admin.Services.Sms;
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
            _timer = new Timer(CheckRentalsForReminders, null, TimeSpan.FromMinutes(2), TimeSpan.FromHours(1));
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
            var reminderTimeUtc = currentTimeUtc.AddHours(24);

            var rentalsToRemind = await db.Rentals
                .Include(r => r.Customer)
                .Include(r => r.Items)
                .Where(r => r.TenantId == company.TenantId
                         && (r.Status == RentalStatus.Active || r.Status == RentalStatus.Confirmed)
                         && r.EndDateUtc <= reminderTimeUtc
                         && r.EndDateUtc > currentTimeUtc)
                .ToListAsync();

            if (!rentalsToRemind.Any()) return;

            // Product names for SMS template
            var productIds = rentalsToRemind.SelectMany(r => r.Items.Select(i => i.ProductId)).Distinct().ToList();
            var products = await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            var emailCount = 0;
            var smsCount = 0;

            foreach (var rental in rentalsToRemind)
            {
                // Email reminder
                if (!string.IsNullOrEmpty(rental.Customer?.Email))
                {
                    await SendReminderEmail(rental, emailService);
                    emailCount++;
                }

                // SMS reminder (only if enabled and not already sent)
                if (company.SmsReminderEnabled
                    && !rental.IsReminderSmsSent
                    && !string.IsNullOrWhiteSpace(rental.Customer?.PhoneNumber))
                {
                    await SendReminderSms(rental, company, products, smsSender, db);
                    smsCount++;
                }
            }

            if (emailCount > 0 || smsCount > 0)
            {
                _logger.LogInformation(
                    "Tenant {TenantId}: wysłano {EmailCount} email i {SmsCount} SMS przypomnień",
                    company.TenantId, emailCount, smsCount);
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
            var endDate = rental.EndDateUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
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

                _logger.LogInformation("Przypomnienie email wysłane do {Email} dla wynajmu {RentalId}",
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