using SportRental.Infrastructure.Domain;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Storage;
using SportRental.Admin.Services.Time;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SportRental.Admin.Services.Contracts
{
    public class QuestPdfContractGenerator : IContractGenerator
    {
        private readonly IFileStorage _fileStorage;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<QuestPdfContractGenerator> _logger;

        public QuestPdfContractGenerator(IFileStorage fileStorage, IEmailSender emailSender, ILogger<QuestPdfContractGenerator> logger)
        {
            _fileStorage = fileStorage;
            _emailSender = emailSender;
            _logger = logger;
            
            // Ustaw licencję QuestPDF - Community jest darmowa dla firm z przychodem < $1M USD
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // Feedback #9: pełny adres firmy na umowie — ulica+numer, "kod miasto", "woj. …".
        // Kod pocztowy/miasto/województwo są w CompanyInfo (auto-fill z mapy), wcześniej nie trafiały na PDF.
        private static IEnumerable<string> BuildCompanyAddressLines(CompanyInfo ci)
        {
            if (!string.IsNullOrWhiteSpace(ci.Address))
                yield return ci.Address!;
            var postalCity = string.Join(" ", new[] { ci.PostalCode, ci.City }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(postalCity))
                yield return postalCity;
            if (!string.IsNullOrWhiteSpace(ci.Voivodeship))
                yield return $"woj. {ci.Voivodeship}";
        }

        public Task<byte[]> GenerateRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
        {
            var productMap = products.ToDictionary(p => p.Id, p => p);
            var rentalDays = Math.Max(1, (rental.EndDateUtc - rental.StartDateUtc).Days);
            var nowLocal = PolishTimeZone.FromUtc(DateTime.UtcNow);
            
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10));
                    
                    // Header z danymi firmy + branding RentSpot
                    page.Header().Column(col =>
                    {
                        // Top brand band — RentSpot (platforma) + slogan
                        col.Item().PaddingBottom(4).Row(brandRow =>
                        {
                            brandRow.RelativeItem().Text(text =>
                            {
                                text.Span("RentSpot").FontSize(13).Bold().FontColor("#2F3C7E");
                                text.Span(".").FontSize(13).Bold().FontColor("#F96167");
                                text.Span("  — platforma obsługi wypożyczalni").FontSize(8).FontColor("#5B6B82");
                            });
                            brandRow.ConstantItem(100).AlignRight().Text("rentspot.eu").FontSize(8).FontColor("#5B6B82");
                        });
                        col.Item().LineHorizontal(0.4f).LineColor("#F96167");

                        // Główny header umowy
                        col.Item().PaddingTop(8).Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Item().Text("UMOWA WYPOŻYCZENIA").Bold().FontSize(18).FontColor("#2F3C7E");
                                left.Item().Text($"Nr: {rental.Id.ToString()[..8].ToUpper()}").FontSize(12);
                                left.Item().Text($"Data: {nowLocal:dd.MM.yyyy}").FontSize(10);
                            });

                            if (companyInfo != null)
                            {
                                row.RelativeItem().AlignRight().Column(right =>
                                {
                                    right.Item().Text(companyInfo.Name ?? "RentSpot").Bold().FontSize(12);
                                    foreach (var line in BuildCompanyAddressLines(companyInfo))
                                        right.Item().Text(line);
                                    if (!string.IsNullOrWhiteSpace(companyInfo.NIP))
                                        right.Item().Text($"NIP: {companyInfo.NIP}");
                                    if (!string.IsNullOrWhiteSpace(companyInfo.REGON))
                                        right.Item().Text($"REGON: {companyInfo.REGON}");
                                    if (!string.IsNullOrWhiteSpace(companyInfo.PhoneNumber))
                                        right.Item().Text($"Tel: {companyInfo.PhoneNumber}");
                                    if (!string.IsNullOrWhiteSpace(companyInfo.Email))
                                        right.Item().Text($"Email: {companyInfo.Email}");
                                });
                            }
                        });
                        col.Item().PaddingTop(10).LineHorizontal(1);
                    });

                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        // Sekcja: Strony umowy
                        col.Item().Text("§1. STRONY UMOWY").Bold().FontSize(12);
                        col.Item().PaddingLeft(10).Column(parties =>
                        {
                            parties.Item().Text("WYPOŻYCZAJĄCY (Wynajmujący):").SemiBold();
                            if (companyInfo != null)
                            {
                                parties.Item().Text($"   {companyInfo.Name}");
                                foreach (var line in BuildCompanyAddressLines(companyInfo))
                                    parties.Item().Text($"   {line}");
                                if (!string.IsNullOrWhiteSpace(companyInfo.NIP))
                                    parties.Item().Text($"   NIP: {companyInfo.NIP}");
                            }
                            else
                            {
                                parties.Item().Text("   RentSpot");
                            }
                            
                            parties.Item().PaddingTop(8).Text("NAJEMCA (Klient):").SemiBold();
                            parties.Item().Text($"   {customer.FullName}");
                            if (!string.IsNullOrWhiteSpace(customer.Email))
                                parties.Item().Text($"   Email: {customer.Email}");
                            if (!string.IsNullOrWhiteSpace(customer.PhoneNumber))
                                parties.Item().Text($"   Tel: {customer.PhoneNumber}");
                            if (!string.IsNullOrWhiteSpace(customer.Address))
                                parties.Item().Text($"   Adres: {customer.Address}");
                            if (!string.IsNullOrWhiteSpace(customer.DocumentNumber))
                                parties.Item().Text($"   Nr dokumentu: {customer.DocumentNumber}");
                        });
                        
                        col.Item().PaddingTop(15);
                        
                        // Sekcja: Okres wypożyczenia
                        col.Item().Text("§2. OKRES WYPOŻYCZENIA").Bold().FontSize(12);
                        col.Item().PaddingLeft(10).Column(period =>
                        {
                            period.Item().Text($"Data rozpoczęcia: {rental.StartDateUtc:dd.MM.yyyy HH:mm}");
                            period.Item().Text($"Data zakończenia: {rental.EndDateUtc:dd.MM.yyyy HH:mm}");
                            period.Item().Text($"Liczba dni: {rentalDays}");
                        });
                        
                        col.Item().PaddingTop(15);
                        
                        // Sekcja: Przedmiot wypożyczenia
                        col.Item().Text("§3. PRZEDMIOT WYPOŻYCZENIA").Bold().FontSize(12);
                        col.Item().PaddingTop(5).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(30);   // Lp.
                                c.RelativeColumn(5);    // Produkt
                                c.ConstantColumn(50);   // Ilość
                                c.ConstantColumn(80);   // Cena/dzień
                                c.ConstantColumn(80);   // Razem
                            });
                            
                            // Header
                            t.Header(h =>
                            {
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Lp.").SemiBold();
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Produkt").SemiBold();
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Ilość").SemiBold();
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Cena/dzień").SemiBold();
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Razem").SemiBold();
                            });
                            
                            var lp = 1;
                            foreach (var it in items)
                            {
                                var p = productMap.GetValueOrDefault(it.ProductId);
                                var bgColor = lp % 2 == 0 ? Colors.Grey.Lighten5 : Colors.White;
                                
                                t.Cell().Background(bgColor).Padding(5).Text(lp.ToString());
                                t.Cell().Background(bgColor).Padding(5).Text(p?.Name ?? it.ProductId.ToString());
                                t.Cell().Background(bgColor).Padding(5).AlignCenter().Text(it.Quantity.ToString());
                                t.Cell().Background(bgColor).Padding(5).AlignRight().Text($"{it.PricePerDay:0.00} zł");
                                t.Cell().Background(bgColor).Padding(5).AlignRight().Text($"{it.Subtotal:0.00} zł");
                                lp++;
                            }
                        });
                        
                        col.Item().PaddingTop(10).AlignRight().Column(summary =>
                        {
                            summary.Item().Text($"SUMA: {rental.TotalAmount:0.00} zł").Bold().FontSize(14);
                            if (rental.DepositAmount > 0)
                            {
                                summary.Item().Text($"Kaucja: {rental.DepositAmount:0.00} zł");
                            }
                        });
                        
                        col.Item().PaddingTop(20);
                        
                        // Sekcja: Warunki
                        col.Item().Text("§4. WARUNKI WYPOŻYCZENIA").Bold().FontSize(12);
                        col.Item().PaddingLeft(10).Column(terms =>
                        {
                            terms.Item().Text("1. Najemca zobowiązuje się do zwrotu sprzętu w stanie niepogorszonym.");
                            terms.Item().Text("2. Najemca ponosi odpowiedzialność za wszelkie uszkodzenia powstałe w trakcie użytkowania.");
                            terms.Item().Text("3. W przypadku opóźnienia w zwrocie naliczana jest opłata zgodna z cennikiem.");
                            terms.Item().Text("4. Kaucja zostanie zwrócona po sprawdzeniu stanu sprzętu.");
                        });
                        
                        if (!string.IsNullOrWhiteSpace(rental.Notes))
                        {
                            col.Item().PaddingTop(15);
                            col.Item().Text("§5. UWAGI").Bold().FontSize(12);
                            col.Item().PaddingLeft(10).Text(rental.Notes);
                        }
                        
                        col.Item().PaddingTop(30);
                        
                        // Podpisy
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Item().PaddingTop(30).Width(150).LineHorizontal(0.5f);
                                left.Item().Text("Podpis Wynajmującego").FontSize(9);
                            });
                            row.RelativeItem().AlignRight().Column(right =>
                            {
                                right.Item().PaddingTop(30).Width(150).LineHorizontal(0.5f);
                                right.Item().Text("Podpis Najemcy").FontSize(9);
                            });
                        });
                    });

                    page.Footer().AlignCenter().Column(footer =>
                    {
                        footer.Item().LineHorizontal(0.5f);
                        footer.Item().PaddingTop(5).Row(row =>
                        {
                            row.RelativeItem().Text(text =>
                            {
                                text.Span(companyInfo?.Name ?? "RentSpot").FontSize(8);
                                text.Span("  ·  via RentSpot").FontSize(7).FontColor("#5B6B82");
                            });
                            row.RelativeItem().AlignCenter().Text(text =>
                            {
                                text.Span("Strona ").FontSize(8);
                                text.CurrentPageNumber().FontSize(8);
                                text.Span(" z ").FontSize(8);
                                text.TotalPages().FontSize(8);
                            });
                            row.RelativeItem().AlignRight().Text(text =>
                            {
                                text.Span("rentspot.eu").FontSize(7).FontColor("#5B6B82");
                                text.Span($"\nWygenerowano: {nowLocal:dd.MM.yyyy HH:mm}").FontSize(7).FontColor("#5B6B82");
                            });
                        });
                    });
                });
            });
            
            var bytes = doc.GeneratePdf();
            return Task.FromResult(bytes);
        }

        public Task<byte[]> GenerateRentalContractAsync(string templateContent, Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
        {
            var productMap = products.ToDictionary(p => p.Id, p => p);
            var filled = FillTemplate(templateContent, rental, items, customer, productMap, companyInfo);
            var nowLocal = PolishTimeZone.FromUtc(DateTime.UtcNow);

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.Content().Text(filled).FontSize(11);
                    page.Footer().AlignCenter().Text($"{companyInfo?.Name ?? "RentSpot"} · via RentSpot · rentspot.eu — {nowLocal:dd.MM.yyyy}").FontSize(8);
                });
            });
            var bytes = doc.GeneratePdf();
            return Task.FromResult(bytes);
        }

        private static string FillTemplate(
            string templateContent,
            Rental rental,
            IEnumerable<RentalItem> items,
            Customer customer,
            IReadOnlyDictionary<Guid, Product> productMap,
            CompanyInfo? companyInfo)
        {
            var itemsLines = string.Join("\n", items.Select(it =>
            {
                var p = productMap.GetValueOrDefault(it.ProductId);
                return $"- {(p?.Name ?? it.ProductId.ToString())} x{it.Quantity} @ {it.PricePerDay:0.00} zł = {it.Subtotal:0.00} zł";
            }));
            var rentalDays = Math.Max(1, (int)Math.Ceiling(
                (rental.EndDateUtc - rental.StartDateUtc).TotalDays));
            var companyAddress = companyInfo == null
                ? ""
                : string.Join(", ", BuildCompanyAddressLines(companyInfo));

            return templateContent
                .Replace("{{CustomerName}}", customer.FullName)
                .Replace("{{CustomerDocument}}", customer.DocumentNumber ?? "")
                .Replace("{{CustomerEmail}}", customer.Email ?? "")
                .Replace("{{CustomerPhone}}", customer.PhoneNumber ?? "")
                .Replace("{{CustomerAddress}}", customer.Address ?? "")
                .Replace("{{StartDate}}", PolishTimeZone.FromUtc(rental.StartDateUtc).ToString("dd.MM.yyyy HH:mm"))
                .Replace("{{EndDate}}", PolishTimeZone.FromUtc(rental.EndDateUtc).ToString("dd.MM.yyyy HH:mm"))
                .Replace("{{RentalDays}}", rentalDays.ToString())
                .Replace("{{RentalId}}", rental.Id.ToString()[..8].ToUpperInvariant())
                .Replace("{{CurrentDate}}", PolishTimeZone.FromUtc(DateTime.UtcNow).ToString("dd.MM.yyyy"))
                .Replace("{{ItemsTable}}", itemsLines)
                .Replace("{{Total}}", rental.TotalAmount.ToString("0.00"))
                .Replace("{{Deposit}}", rental.DepositAmount.ToString("0.00"))
                .Replace("{{CompanyName}}", companyInfo?.Name ?? "RentSpot")
                .Replace("{{CompanyAddress}}", companyAddress)
                .Replace("{{CompanyPostalCode}}", companyInfo?.PostalCode ?? "")
                .Replace("{{CompanyCity}}", companyInfo?.City ?? "")
                .Replace("{{CompanyVoivodeship}}", companyInfo?.Voivodeship ?? "")
                .Replace("{{CompanyNIP}}", companyInfo?.NIP ?? "")
                .Replace("{{CompanyREGON}}", companyInfo?.REGON ?? "")
                .Replace("{{CompanyPhone}}", companyInfo?.PhoneNumber ?? "")
                .Replace("{{CompanyEmail}}", companyInfo?.Email ?? "");
        }

        public async Task<string> GenerateAndSaveRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, string? templateContent = null, CancellationToken ct = default)
        {
            if (rental == null) throw new ArgumentNullException(nameof(rental));
            if (customer == null) throw new ArgumentNullException(nameof(customer));
            if (items == null || !items.Any()) throw new ArgumentException("Rental items cannot be null or empty.", nameof(items));
            if (products == null) throw new ArgumentNullException(nameof(products));

            var contractBytes = string.IsNullOrWhiteSpace(templateContent)
                ? await GenerateRentalContractAsync(rental, items, customer, products, companyInfo, ct)
                : await GenerateRentalContractAsync(templateContent, rental, items, customer, products, companyInfo, ct);
            
            var fileName = $"umowa_{rental.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
            var filePath = $"contracts/{rental.TenantId}/{fileName}";
            
            var storageReference = await _fileStorage.SavePrivateAsync(filePath, contractBytes, ct);

            _logger.LogInformation("Umowa zapisana w {FilePath} dla wynajmu {RentalId}", filePath, rental.Id);

            return storageReference;
        }

        public async Task SendRentalContractByEmailAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
        {
            if (rental == null) throw new ArgumentNullException(nameof(rental));
            if (customer == null) throw new ArgumentNullException(nameof(customer));
            if (items == null || !items.Any()) throw new ArgumentException("Rental items cannot be null or empty.", nameof(items));
            if (products == null) throw new ArgumentNullException(nameof(products));

            var contractBytes = await GenerateRentalContractAsync(rental, items, customer, products, companyInfo, ct);
            
            if (string.IsNullOrWhiteSpace(customer.Email))
            {
                _logger.LogWarning("Brak adresu email dla klienta {CustomerId}", customer.Id);
                throw new ArgumentException($"Klient {customer.FullName} nie ma adresu email", nameof(customer));
            }
            
            await _emailSender.SendRentalContractAsync(customer.Email, customer.FullName ?? "Klient", contractBytes);
            
            _logger.LogInformation("Umowa wysłana emailem do {Email} dla wynajmu {RentalId}", customer.Email, rental.Id);
        }

        public async Task SendRentalConfirmationEmailAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, string? templateContent = null, CancellationToken ct = default)
        {
            if (rental == null) throw new ArgumentNullException(nameof(rental));
            if (customer == null) throw new ArgumentNullException(nameof(customer));
            if (items == null || !items.Any()) throw new ArgumentException("Rental items cannot be null or empty.", nameof(items));
            if (products == null) throw new ArgumentNullException(nameof(products));

            if (string.IsNullOrWhiteSpace(customer.Email))
            {
                _logger.LogWarning("Brak adresu email dla klienta {CustomerId} - pomijam wysyłkę emaila", customer.Id);
                return;
            }

            // Generuj PDF umowy
            var contractBytes = string.IsNullOrWhiteSpace(templateContent)
                ? await GenerateRentalContractAsync(rental, items, customer, products, companyInfo, ct)
                : await GenerateRentalContractAsync(templateContent, rental, items, customer, products, companyInfo, ct);
            
            // Generuj HTML emaila
            var productMap = products.ToDictionary(p => p.Id, p => p);
            var rentalDays = Math.Max(1, (rental.EndDateUtc - rental.StartDateUtc).Days);
            var companyName = companyInfo?.Name ?? "RentSpot";
            var companyEmail = companyInfo?.Email ?? "kontakt@rentspot.eu";
            var companyPhone = companyInfo?.PhoneNumber ?? "";

            var htmlBody = GenerateConfirmationEmailHtml(rental, items.ToList(), customer, productMap, companyInfo, rentalDays);
            
            // Zapisz PDF do pliku tymczasowego
            var tempPdfPath = Path.Combine(Path.GetTempPath(), $"umowa_{rental.Id}_{Guid.NewGuid()}.pdf");
            await File.WriteAllBytesAsync(tempPdfPath, contractBytes, ct);
            
            try
            {
                var subject = $"🎿 Potwierdzenie wypożyczenia - {companyName} #{rental.Id.ToString()[..8]}";
                await _emailSender.SendEmailWithAttachmentAsync(customer.Email, subject, htmlBody, tempPdfPath);
                _logger.LogInformation("Email potwierdzenia wysłany do {Email} dla wynajmu {RentalId}", customer.Email, rental.Id);
            }
            finally
            {
                // Usuń plik tymczasowy
                try { File.Delete(tempPdfPath); } catch { /* ignore */ }
            }
        }

        private string GenerateConfirmationEmailHtml(Rental rental, List<RentalItem> items, Customer customer, Dictionary<Guid, Product> productMap, CompanyInfo? companyInfo, int rentalDays)
        {
            var companyName = companyInfo?.Name ?? "SportRental";
            var companyEmail = companyInfo?.Email ?? "sportrental.kontakt@gmail.com";
            var companyPhone = companyInfo?.PhoneNumber ?? "";
            var companyAddress = companyInfo?.Address ?? "";
            
            var itemsHtml = string.Join("", items.Select(it =>
            {
                var p = productMap.GetValueOrDefault(it.ProductId);
                return $@"
                    <tr>
                        <td style='padding: 12px; border-bottom: 1px solid #e5e7eb;'><strong>{p?.Name ?? "Produkt"}</strong></td>
                        <td style='padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: center;'>{it.Quantity}</td>
                        <td style='padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: right;'>{it.PricePerDay:0.00} zł</td>
                        <td style='padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: right; color: #667eea; font-weight: 600;'>{it.Subtotal:0.00} zł</td>
                    </tr>";
            }));

            return $@"
<!DOCTYPE html>
<html lang='pl'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Potwierdzenie wypożyczenia</title>
</head>
<body style='font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; background-color: #f5f5f5;'>
    <div style='background-color: #ffffff; border-radius: 12px; box-shadow: 0 4px 6px rgba(0,0,0,0.1); overflow: hidden;'>
        
        <!-- Header -->
        <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px 20px; text-align: center;'>
            <h1 style='margin: 0; font-size: 26px; font-weight: 600;'>🎿 {companyName}</h1>
            <p style='margin-top: 8px; font-size: 14px; opacity: 0.9;'>Dziękujemy za wypożyczenie!</p>
        </div>
        
        <!-- Content -->
        <div style='padding: 30px 20px;'>
            <div style='background-color: #10b981; color: white; padding: 10px 20px; border-radius: 25px; display: inline-block; font-weight: 600; font-size: 14px; margin-bottom: 20px;'>
                ✓ Rezerwacja potwierdzona
            </div>
            
            <p style='font-size: 16px; margin-bottom: 24px;'>
                Cześć <strong>{customer.FullName}</strong>,
            </p>
            
            <p style='margin-bottom: 24px;'>
                Twoja rezerwacja została potwierdzona! W załączniku znajdziesz umowę wypożyczenia w formacie PDF.
            </p>

            <!-- Szczegóły rezerwacji -->
            <div style='margin-bottom: 30px;'>
                <h3 style='font-size: 16px; font-weight: 600; color: #1f2937; margin-bottom: 12px; border-bottom: 2px solid #e5e7eb; padding-bottom: 8px;'>📅 Szczegóły rezerwacji</h3>
                <div style='background-color: #f9fafb; border-left: 4px solid #667eea; padding: 16px; border-radius: 4px;'>
                    <table style='width: 100%;'>
                        <tr>
                            <td style='padding: 6px 0; font-weight: 600; color: #6b7280;'>Numer rezerwacji:</td>
                            <td style='padding: 6px 0; text-align: right;'><strong>#{rental.Id.ToString()[..8].ToUpper()}</strong></td>
                        </tr>
                        <tr>
                            <td style='padding: 6px 0; font-weight: 600; color: #6b7280;'>Data rozpoczęcia:</td>
                            <td style='padding: 6px 0; text-align: right;'>{rental.StartDateUtc:dd MMMM yyyy, HH:mm}</td>
                        </tr>
                        <tr>
                            <td style='padding: 6px 0; font-weight: 600; color: #6b7280;'>Data zakończenia:</td>
                            <td style='padding: 6px 0; text-align: right;'>{rental.EndDateUtc:dd MMMM yyyy, HH:mm}</td>
                        </tr>
                        <tr>
                            <td style='padding: 6px 0; font-weight: 600; color: #6b7280;'>Liczba dni:</td>
                            <td style='padding: 6px 0; text-align: right;'><strong>{rentalDays} dni</strong></td>
                        </tr>
                    </table>
                </div>
            </div>

            <!-- Wypożyczone produkty -->
            <div style='margin-bottom: 30px;'>
                <h3 style='font-size: 16px; font-weight: 600; color: #1f2937; margin-bottom: 12px; border-bottom: 2px solid #e5e7eb; padding-bottom: 8px;'>🎿 Wypożyczony sprzęt</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <thead>
                        <tr style='background-color: #f3f4f6;'>
                            <th style='padding: 12px; text-align: left; font-weight: 600; color: #374151;'>Produkt</th>
                            <th style='padding: 12px; text-align: center; font-weight: 600; color: #374151;'>Ilość</th>
                            <th style='padding: 12px; text-align: right; font-weight: 600; color: #374151;'>Cena/dzień</th>
                            <th style='padding: 12px; text-align: right; font-weight: 600; color: #374151;'>Razem</th>
                        </tr>
                    </thead>
                    <tbody>
                        {itemsHtml}
                    </tbody>
                </table>
            </div>

            <!-- Podsumowanie finansowe -->
            <div style='margin-bottom: 30px;'>
                <h3 style='font-size: 16px; font-weight: 600; color: #1f2937; margin-bottom: 12px; border-bottom: 2px solid #e5e7eb; padding-bottom: 8px;'>💰 Podsumowanie</h3>
                <div style='background-color: #f9fafb; border-left: 4px solid #10b981; padding: 16px; border-radius: 4px;'>
                    <table style='width: 100%;'>
                        <tr>
                            <td style='padding: 6px 0; font-weight: 600; color: #6b7280;'>Wartość wypożyczenia:</td>
                            <td style='padding: 6px 0; text-align: right;'>{rental.TotalAmount:0.00} zł</td>
                        </tr>
                        {(rental.DepositAmount > 0 ? $@"
                        <tr>
                            <td style='padding: 6px 0; font-weight: 600; color: #6b7280;'>Kaucja:</td>
                            <td style='padding: 6px 0; text-align: right; color: #667eea; font-weight: 600;'>{rental.DepositAmount:0.00} zł</td>
                        </tr>" : "")}
                        <tr style='border-top: 2px solid #e5e7eb;'>
                            <td style='padding: 12px 0 6px 0; font-weight: 700; font-size: 16px;'>RAZEM DO ZAPŁATY:</td>
                            <td style='padding: 12px 0 6px 0; text-align: right; font-weight: 700; font-size: 16px; color: #667eea;'>{rental.TotalAmount:0.00} zł</td>
                        </tr>
                    </table>
                </div>
            </div>

            <!-- Ważne informacje -->
            <div style='background-color: #fef3c7; border-left: 4px solid #f59e0b; padding: 16px; border-radius: 4px; margin-bottom: 30px;'>
                <p style='margin: 0; font-weight: 600;'>ℹ️ Ważne informacje:</p>
                <ul style='margin: 12px 0 0 0; padding-left: 20px;'>
                    <li>Stawić się w punkcie wypożyczalni w dniu <strong>{rental.StartDateUtc:dd.MM.yyyy}</strong></li>
                    <li>Zabrać ze sobą <strong>dokument tożsamości</strong></li>
                    <li>Sprawdzić stan sprzętu przy odbiorze</li>
                </ul>
            </div>

            <!-- Kontakt -->
            <div style='text-align: center; margin-top: 30px;'>
                <p style='color: #6b7280; font-size: 14px;'>
                    W razie pytań, skontaktuj się z nami:<br>
                    {(string.IsNullOrWhiteSpace(companyEmail) ? "" : $"📧 <a href='mailto:{companyEmail}' style='color: #667eea;'>{companyEmail}</a><br>")}
                    {(string.IsNullOrWhiteSpace(companyPhone) ? "" : $"📞 <strong>{companyPhone}</strong><br>")}
                    {(string.IsNullOrWhiteSpace(companyAddress) ? "" : $"📍 {companyAddress}")}
                </p>
            </div>
        </div>
        
        <!-- Footer -->
        <div style='background-color: #f9fafb; padding: 20px; text-align: center; border-top: 1px solid #e5e7eb;'>
            <p style='margin: 5px 0; font-weight: 600;'>{companyName}</p>
            <p style='margin: 5px 0; color: #6b7280; font-size: 14px;'>Profesjonalne wypożyczalnie sprzętu sportowego</p>
            <p style='font-size: 12px; color: #9ca3af; margin-top: 16px;'>
                Ten email został wysłany automatycznie. W załączniku znajduje się umowa wypożyczenia.
            </p>
        </div>
    </div>
</body>
</html>";
        }
    }
}
