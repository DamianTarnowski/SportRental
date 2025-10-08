using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SportRental.Infrastructure.Domain;

namespace SportRental.Api.Services.Contracts;

/// <summary>
/// Service for generating beautiful rental contract PDFs using QuestPDF
/// </summary>
public class PdfContractService : IPdfContractService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PdfContractService> _logger;

    public PdfContractService(
        IWebHostEnvironment environment,
        ILogger<PdfContractService> logger)
    {
        _environment = environment;
        _logger = logger;
        
        // QuestPDF License (Community License for free use)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> GenerateContractPdfAsync(
        Rental rental,
        Customer customer,
        List<(Product product, int quantity)> items,
        CompanyInfo? companyInfo = null)
    {
        var rentalDays = (rental.EndDateUtc - rental.StartDateUtc).Days;
        if (rentalDays <= 0) rentalDays = 1;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                
                // Header
                page.Header().Column(header =>
                {
                    header.Item().AlignCenter().Text("UMOWA WYPOŻYCZENIA SPRZĘTU SPORTOWEGO")
                        .FontSize(18)
                        .Bold()
                        .FontColor(Colors.Purple.Darken2);
                    
                    header.Item().PaddingTop(10).AlignCenter().Text($"Nr rezerwacji: {rental.Id.ToString()[..8].ToUpper()}")
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken1);
                    
                    header.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Purple.Lighten2);
                });

                // Content
                page.Content().PaddingVertical(20).Column(content =>
                {
                    // Company + Customer Info Section (side-by-side)
                    content.Item().PaddingBottom(15).Row(row =>
                    {
                        // COMPANY INFO (LEFT)
                        if (companyInfo != null)
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("DANE WYPOŻYCZALNI").FontSize(14).Bold().FontColor(Colors.Purple.Darken1);
                                col.Item().PaddingTop(8).Column(c =>
                                {
                                    if (!string.IsNullOrEmpty(companyInfo.Name))
                                    {
                                        c.Item().Text(text =>
                                        {
                                            text.Span(companyInfo.Name).SemiBold().FontSize(11);
                                        });
                                    }
                                    if (!string.IsNullOrEmpty(companyInfo.Address))
                                    {
                                        c.Item().PaddingTop(2).Text(text =>
                                        {
                                            text.Span("Adres: ").SemiBold().FontSize(9);
                                            text.Span(companyInfo.Address).FontSize(9);
                                        });
                                    }
                                    if (!string.IsNullOrEmpty(companyInfo.NIP))
                                    {
                                        c.Item().PaddingTop(2).Text(text =>
                                        {
                                            text.Span("NIP: ").SemiBold().FontSize(9);
                                            text.Span(companyInfo.NIP).FontSize(9);
                                        });
                                    }
                                    if (!string.IsNullOrEmpty(companyInfo.REGON))
                                    {
                                        c.Item().PaddingTop(2).Text(text =>
                                        {
                                            text.Span("REGON: ").SemiBold().FontSize(9);
                                            text.Span(companyInfo.REGON).FontSize(9);
                                        });
                                    }
                                    if (!string.IsNullOrEmpty(companyInfo.Email))
                                    {
                                        c.Item().PaddingTop(2).Text(text =>
                                        {
                                            text.Span("Email: ").SemiBold().FontSize(9);
                                            text.Span(companyInfo.Email).FontSize(9);
                                        });
                                    }
                                    if (!string.IsNullOrEmpty(companyInfo.PhoneNumber))
                                    {
                                        c.Item().PaddingTop(2).Text(text =>
                                        {
                                            text.Span("Tel: ").SemiBold().FontSize(9);
                                            text.Span(companyInfo.PhoneNumber).FontSize(9);
                                        });
                                    }
                                });
                            });
                            row.AutoItem().Width(30); // Spacer
                        }

                        // CUSTOMER INFO (RIGHT)
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("DANE KLIENTA").FontSize(14).Bold().FontColor(Colors.Purple.Darken1);
                            col.Item().PaddingTop(8).Row(innerRow =>
                            {
                                innerRow.RelativeItem().Column(c =>
                                {
                                    c.Item().Text(text =>
                                    {
                                        text.Span("Imię i nazwisko: ").SemiBold();
                                        text.Span(customer.FullName);
                                    });
                                    c.Item().Text(text =>
                                    {
                                        text.Span("Email: ").SemiBold();
                                        text.Span(customer.Email);
                                    });
                                });
                                innerRow.RelativeItem().Column(c =>
                                {
                                    c.Item().Text(text =>
                                    {
                                        text.Span("Telefon: ").SemiBold();
                                        text.Span(customer.PhoneNumber ?? "---");
                                    });
                                    c.Item().Text(text =>
                                    {
                                        text.Span("Dokument: ").SemiBold();
                                        text.Span(customer.DocumentNumber ?? "---");
                                    });
                                });
                            });
                        });
                    });

                    // Rental Period Section
                    content.Item().PaddingBottom(15).Column(col =>
                    {
                        col.Item().Text("OKRES WYPOŻYCZENIA").FontSize(14).Bold().FontColor(Colors.Purple.Darken1);
                        col.Item().PaddingTop(8).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(text =>
                                {
                                    text.Span("Data rozpoczęcia: ").SemiBold();
                                    text.Span(rental.StartDateUtc.ToString("dd.MM.yyyy HH:mm"));
                                });
                            });
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(text =>
                                {
                                    text.Span("Data zakończenia: ").SemiBold();
                                    text.Span(rental.EndDateUtc.ToString("dd.MM.yyyy HH:mm"));
                                });
                            });
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(text =>
                                {
                                    text.Span("Liczba dni: ").SemiBold();
                                    text.Span($"{rentalDays}");
                                });
                            });
                        });
                    });

                    // Products Table Section
                    content.Item().PaddingBottom(15).Column(col =>
                    {
                        col.Item().Text("WYPOŻYCZONY SPRZĘT").FontSize(14).Bold().FontColor(Colors.Purple.Darken1);
                        col.Item().PaddingTop(8).Table(table =>
                        {
                            // Define columns
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4); // Product name
                                columns.RelativeColumn(1); // Quantity
                                columns.RelativeColumn(2); // Price/day
                                columns.RelativeColumn(1); // Days
                                columns.RelativeColumn(2); // Total
                            });

                            // Header
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Purple.Lighten3)
                                    .Padding(8).Text("Produkt").SemiBold();
                                header.Cell().Background(Colors.Purple.Lighten3)
                                    .Padding(8).AlignCenter().Text("Ilość").SemiBold();
                                header.Cell().Background(Colors.Purple.Lighten3)
                                    .Padding(8).AlignRight().Text("Cena/dzień").SemiBold();
                                header.Cell().Background(Colors.Purple.Lighten3)
                                    .Padding(8).AlignCenter().Text("Dni").SemiBold();
                                header.Cell().Background(Colors.Purple.Lighten3)
                                    .Padding(8).AlignRight().Text("Razem").SemiBold();
                            });

                            // Items
                            foreach (var (product, quantity) in items)
                            {
                                var itemTotal = product.DailyPrice * quantity * rentalDays;
                                
                                table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2)
                                    .Padding(8).Text(product.Name);
                                table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2)
                                    .Padding(8).AlignCenter().Text(quantity.ToString());
                                table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2)
                                    .Padding(8).AlignRight().Text($"{product.DailyPrice:F2} zł");
                                table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2)
                                    .Padding(8).AlignCenter().Text(rentalDays.ToString());
                                table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2)
                                    .Padding(8).AlignRight().Text($"{itemTotal:F2} zł");
                            }
                        });
                    });

                    // Financial Summary Section
                    content.Item().PaddingBottom(15).Column(col =>
                    {
                        col.Item().Text("PODSUMOWANIE FINANSOWE").FontSize(14).Bold().FontColor(Colors.Purple.Darken1);
                        col.Item().PaddingTop(8).Background(Colors.Grey.Lighten4).Padding(12).Column(summary =>
                        {
                            summary.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Wartość wypożyczenia:").SemiBold();
                                row.AutoItem().Text($"{rental.TotalAmount:F2} zł").SemiBold();
                            });
                            summary.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Kaucja (30%):");
                                row.AutoItem().Text($"{rental.DepositAmount:F2} zł");
                            });
                            summary.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                            summary.Item().PaddingTop(5).Row(row =>
                            {
                                row.RelativeItem().Text("Zapłacono online:").Bold().FontColor(Colors.Green.Darken1);
                                row.AutoItem().Text($"{rental.DepositAmount:F2} zł").Bold().FontColor(Colors.Green.Darken1);
                            });
                            summary.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Do zapłaty przy odbiorze:").Bold().FontColor(Colors.Red.Darken1);
                                row.AutoItem().Text($"{(rental.TotalAmount - rental.DepositAmount):F2} zł").Bold().FontColor(Colors.Red.Darken1);
                            });
                        });
                    });

                    // Terms & Conditions Section
                    content.Item().PaddingBottom(15).Column(col =>
                    {
                        col.Item().Text("WARUNKI WYPOŻYCZENIA").FontSize(14).Bold().FontColor(Colors.Purple.Darken1);
                        col.Item().PaddingTop(8).Column(terms =>
                        {
                            terms.Item().Text("1. Klient zobowiązuje się do zwrotu sprzętu w stanie nienaruszonym w terminie określonym w umowie.");
                            terms.Item().Text("2. Za uszkodzenie lub zniszczenie sprzętu klient ponosi pełną odpowiedzialność finansową.");
                            terms.Item().Text("3. W przypadku zwłoki w zwrocie sprzętu, naliczana jest opłata za każdy dodatkowy dzień.");
                            terms.Item().Text("4. Kaucja zostanie zwrócona po sprawdzeniu stanu technicznego sprzętu.");
                            terms.Item().Text("5. Klient potwierdza, że otrzymał sprzęt w pełni sprawny i kompletny.");
                        });
                    });

                    // Signatures Section
                    content.Item().PaddingTop(20).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().BorderBottom(1).BorderColor(Colors.Grey.Darken1).Text(" ");
                            col.Item().PaddingTop(5).AlignCenter().Text("Podpis klienta").FontSize(10).FontColor(Colors.Grey.Darken1);
                        });
                        row.AutoItem().Width(50);
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().BorderBottom(1).BorderColor(Colors.Grey.Darken1).Text(" ");
                            col.Item().PaddingTop(5).AlignCenter().Text("Podpis wypożyczającego").FontSize(10).FontColor(Colors.Grey.Darken1);
                        });
                    });
                });

                // Footer
                page.Footer().AlignCenter().Column(footer =>
                {
                    footer.Item().LineHorizontal(1).LineColor(Colors.Purple.Lighten2);
                    footer.Item().PaddingTop(10).Text(text =>
                    {
                        text.Span(companyInfo?.Name ?? "SportRental").SemiBold().FontColor(Colors.Purple.Darken1);
                        text.Span(" | ");
                        text.Span($"Wygenerowano: {DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                    
                    var contactEmail = companyInfo?.Email ?? "kontakt@sportrental.pl";
                    var contactPhone = companyInfo?.PhoneNumber ?? "+48 123 456 789";
                    footer.Item().Text($"{contactEmail} | {contactPhone}").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        var pdfBytes = document.GeneratePdf();
        _logger.LogInformation("Generated contract PDF for rental {RentalId}, size: {Size} bytes", rental.Id, pdfBytes.Length);
        
        return Task.FromResult(pdfBytes);
    }

    public async Task<string> GenerateAndSaveContractPdfAsync(
        Rental rental,
        Customer customer,
        List<(Product product, int quantity)> items,
        CompanyInfo? companyInfo = null)
    {
        // Generate PDF
        var pdfBytes = await GenerateContractPdfAsync(rental, customer, items, companyInfo);

        // Create contracts directory if not exists
        var contractsDir = Path.Combine(_environment.WebRootPath ?? "wwwroot", "contracts", rental.TenantId.ToString());
        if (!Directory.Exists(contractsDir))
        {
            Directory.CreateDirectory(contractsDir);
        }

        // Save to disk
        var fileName = $"umowa_{rental.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
        var filePath = Path.Combine(contractsDir, fileName);
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        // Return public URL (for download)
        var publicUrl = $"/contracts/{rental.TenantId}/{fileName}";
        
        _logger.LogInformation("Saved contract PDF to {FilePath} for rental {RentalId}", filePath, rental.Id);

        return publicUrl;
    }
}
