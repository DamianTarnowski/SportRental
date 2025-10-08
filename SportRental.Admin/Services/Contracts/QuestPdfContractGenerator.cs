using SportRental.Infrastructure.Domain;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Storage;
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
        }
        public Task<byte[]> GenerateRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CancellationToken ct = default)
        {
            var productMap = products.ToDictionary(p => p.Id, p => p);
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.Header().Text("Umowa wynajmu").SemiBold().FontSize(22);
                    page.Content().Column(col =>
                    {
                        col.Item().Text($"Klient: {customer.FullName} ({customer.Email}, {customer.PhoneNumber})");
                        col.Item().Text($"Okres: {rental.StartDateUtc:yyyy-MM-dd} - {rental.EndDateUtc:yyyy-MM-dd}");
                        col.Item().Text("Pozycje:");
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(6);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Text("Produkt");
                                h.Cell().Text("Ilość");
                                h.Cell().Text("Cena/dzień");
                                h.Cell().Text("Razem");
                            });
                            foreach (var it in items)
                            {
                                var p = productMap.GetValueOrDefault(it.ProductId);
                                t.Cell().Text(p?.Name ?? it.ProductId.ToString());
                                t.Cell().Text(it.Quantity.ToString());
                                t.Cell().Text($"{it.PricePerDay:0.00} zł");
                                t.Cell().Text($"{it.Subtotal:0.00} zł");
                            }
                        });
                        col.Item().Text($"Suma: {rental.TotalAmount:0.00} zł").Bold();
                    });
                    page.Footer().AlignRight().Text($"SportRental - {DateTime.Now:yyyy-MM-dd}");
                });
            });
            var bytes = doc.GeneratePdf();
            return Task.FromResult(bytes);
        }

        public Task<byte[]> GenerateRentalContractAsync(string templateContent, Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CancellationToken ct = default)
        {
            // proste podstawienie placeholderów i generacja PDF z jedną sekcją tekstową
            var productMap = products.ToDictionary(p => p.Id, p => p);
            var itemsLines = string.Join("\n", items.Select(it =>
            {
                var p = productMap.GetValueOrDefault(it.ProductId);
                return $"- {(p?.Name ?? it.ProductId.ToString())} x{it.Quantity} @ {it.PricePerDay:0.00} zł = {it.Subtotal:0.00} zł";
            }));

            var filled = templateContent
                .Replace("{{CustomerName}}", customer.FullName)
                .Replace("{{StartDate}}", rental.StartDateUtc.ToString("yyyy-MM-dd"))
                .Replace("{{EndDate}}", rental.EndDateUtc.ToString("yyyy-MM-dd"))
                .Replace("{{ItemsTable}}", itemsLines)
                .Replace("{{Total}}", rental.TotalAmount.ToString("0.00"));

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.Content().Text(filled).FontSize(12);
                });
            });
            var bytes = doc.GeneratePdf();
            return Task.FromResult(bytes);
        }

        public async Task<string> GenerateAndSaveRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CancellationToken ct = default)
        {
            // Walidacja parametrów
            if (rental == null)
                throw new ArgumentNullException(nameof(rental));
            if (customer == null)
                throw new ArgumentNullException(nameof(customer));
            if (items == null || !items.Any())
                throw new ArgumentException("Rental items cannot be null or empty.", nameof(items));
            if (products == null)
                throw new ArgumentNullException(nameof(products));

            var contractBytes = await GenerateRentalContractAsync(rental, items, customer, products, ct);
            
            var fileName = $"umowa_{rental.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var filePath = $"contracts/{rental.TenantId}/{fileName}";
            
            var url = await _fileStorage.SaveAsync(filePath, contractBytes, ct);
            
            _logger.LogInformation("Umowa zapisana w {FilePath} dla wynajmu {RentalId}", filePath, rental.Id);
            
            return url;
        }

        public async Task SendRentalContractByEmailAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CancellationToken ct = default)
        {
            // Walidacja parametrów
            if (rental == null)
                throw new ArgumentNullException(nameof(rental));
            if (customer == null)
                throw new ArgumentNullException(nameof(customer));
            if (items == null || !items.Any())
                throw new ArgumentException("Rental items cannot be null or empty.", nameof(items));
            if (products == null)
                throw new ArgumentNullException(nameof(products));

            var contractBytes = await GenerateRentalContractAsync(rental, items, customer, products, ct);
            
            if (string.IsNullOrWhiteSpace(customer.Email))
            {
                _logger.LogWarning("Brak adresu email dla klienta {CustomerId}", customer.Id);
                throw new ArgumentException($"Klient {customer.FullName} nie ma adresu email", nameof(customer));
            }
            
            await _emailSender.SendRentalContractAsync(customer.Email, customer.FullName, contractBytes);
            
            _logger.LogInformation("Umowa wysłana emailem do {Email} dla wynajmu {RentalId}", customer.Email, rental.Id);
        }
    }
}

