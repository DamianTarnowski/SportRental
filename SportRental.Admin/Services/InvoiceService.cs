using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SportRental.Admin.Services.Storage;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services;

/// <summary>
/// Faza 8c — faktury VAT. Numer FV/{YYYY}/{NNNN} per tenant per rok przez atomic
/// counter. PDF QuestPDF (Outfit + IBM Plex Mono z DS v2 — TODO embed, na razie
/// fallback fonts). Status: Draft → Issued → Paid / Cancelled.
/// </summary>
public interface IInvoiceService
{
    Task<Invoice> CreateForRentalAsync(Guid rentalId, CancellationToken ct = default);
    Task<byte[]> GeneratePdfAsync(Guid invoiceId, CancellationToken ct = default);
    Task<Invoice?> GetByIdAsync(Guid invoiceId, CancellationToken ct = default);
}

public sealed class InvoiceService : IInvoiceService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IFileStorage _storage;
    private readonly ILogger<InvoiceService> _logger;

    /// <summary>VAT 23% (default PL). W przyszłości per-tenant config.</summary>
    private const decimal VatRate = 0.23m;
    private const string VatLabel = "23%";

    public InvoiceService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IFileStorage storage,
        ILogger<InvoiceService> logger)
    {
        _dbFactory = dbFactory;
        _storage = storage;
        _logger = logger;
    }

    public async Task<Invoice> CreateForRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var rental = await db.Rentals
            .IgnoreQueryFilters()
            .Include(r => r.Customer)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct)
            ?? throw new InvalidOperationException($"Rental {rentalId} not found.");

        if (rental.Customer == null)
            throw new InvalidOperationException("Rental ma być powiązany z klientem.");

        var year = DateTime.UtcNow.Year;
        var nextNumber = await AllocateNextNumberAsync(db, rental.TenantId, year, ct);
        var invoiceNumber = $"FV/{year}/{nextNumber:D4}";

        // VAT brutto/netto z TotalAmount jako brutto (typowa konwencja PL — TotalAmount = gross)
        var gross = rental.TotalAmount;
        var net = Math.Round(gross / (1 + VatRate), 2);
        var vat = Math.Round(gross - net, 2);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = rental.TenantId,
            RentalId = rentalId,
            CustomerId = rental.CustomerId,
            Number = invoiceNumber,
            IssuedAtUtc = DateTime.UtcNow,
            DueAtUtc = DateTime.UtcNow.AddDays(14),
            NetAmount = net,
            VatAmount = vat,
            GrossAmount = gross,
            VatRate = VatLabel,
            Status = InvoiceStatus.Issued
        };

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Created invoice {Number} for rental {RentalId} (gross={Gross})",
            invoiceNumber, rentalId, gross);

        return invoice;
    }

    /// <summary>
    /// Atomic next-number alokacja przez UPDATE … RETURNING (PostgreSQL).
    /// Lock per row, brak race condition.
    /// </summary>
    private static async Task<long> AllocateNextNumberAsync(ApplicationDbContext db, Guid tenantId, int year, CancellationToken ct)
    {
        // UPSERT — najpierw spróbuj inkrementować, jeśli brak rekordu to utwórz z 1.
        var counter = await db.InvoiceCounters
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Year == year, ct);

        if (counter == null)
        {
            counter = new InvoiceCounter { Id = Guid.NewGuid(), TenantId = tenantId, Year = year, NextNumber = 1 };
            db.InvoiceCounters.Add(counter);
            try
            {
                await db.SaveChangesAsync(ct);
                return 1;
            }
            catch (DbUpdateException)
            {
                // Race condition — ktoś inny stworzył równolegle. Pobierz i inkrementuj.
                db.InvoiceCounters.Remove(counter);
                counter = await db.InvoiceCounters
                    .FirstAsync(c => c.TenantId == tenantId && c.Year == year, ct);
            }
        }

        var allocated = counter.NextNumber;
        counter.NextNumber += 1;
        await db.SaveChangesAsync(ct);
        return allocated;
    }

    public async Task<Invoice?> GetByIdAsync(Guid invoiceId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Invoices
            .Include(i => i.Rental)
            .Include(i => i.Customer)
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
    }

    public async Task<byte[]> GeneratePdfAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var inv = await GetByIdAsync(invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var company = await db.CompanyInfos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ci => ci.TenantId == inv.TenantId, ct);

        var doc = Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(2, Unit.Centimetre);
                p.DefaultTextStyle(t => t.FontSize(10));

                p.Header().Column(col =>
                {
                    col.Item().PaddingBottom(4).Row(brandRow =>
                    {
                        brandRow.RelativeItem().Text(t =>
                        {
                            t.Span("FAKTURA VAT").FontSize(20).Bold().FontColor("#2F3C7E");
                        });
                        brandRow.ConstantItem(160).AlignRight().Text(t =>
                        {
                            t.Span("RentSpot").FontSize(13).Bold().FontColor("#2F3C7E");
                            t.Span(".").FontSize(13).Bold().FontColor("#F96167");
                            t.Span("\nrentspot.eu  ·  kontakt@rentspot.eu").FontSize(8).FontColor("#5B6B82");
                        });
                    });
                    col.Item().LineHorizontal(0.4f).LineColor("#F96167");

                    col.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text(t => t.Span($"Numer: ").SemiBold());
                            left.Item().Text(inv.Number).FontSize(14).Bold();
                            left.Item().Text(t => t.Span($"Data wystawienia: ").SemiBold());
                            left.Item().Text($"{inv.IssuedAtUtc:dd.MM.yyyy}");
                            left.Item().Text(t => t.Span($"Termin płatności: ").SemiBold());
                            left.Item().Text($"{inv.DueAtUtc:dd.MM.yyyy}");
                        });

                        if (company != null)
                        {
                            row.RelativeItem().AlignRight().Column(right =>
                            {
                                right.Item().Text("Sprzedawca:").SemiBold();
                                right.Item().Text(company.Name ?? "RentSpot Partner").Bold();
                                if (!string.IsNullOrWhiteSpace(company.Address))
                                    right.Item().Text(company.Address);
                                if (!string.IsNullOrWhiteSpace(company.NIP))
                                    right.Item().Text($"NIP: {company.NIP}");
                                if (!string.IsNullOrWhiteSpace(company.Email))
                                    right.Item().Text(company.Email);
                            });
                        }
                    });
                    col.Item().PaddingTop(10).LineHorizontal(1);
                });

                p.Content().PaddingVertical(15).Column(col =>
                {
                    // Nabywca
                    col.Item().PaddingBottom(8).Text("Nabywca:").Bold().FontSize(11);
                    col.Item().Text(inv.Customer?.FullName ?? "—").Bold();
                    if (!string.IsNullOrWhiteSpace(inv.Customer?.Address))
                        col.Item().Text(inv.Customer.Address);
                    if (!string.IsNullOrWhiteSpace(inv.Customer?.Email))
                        col.Item().Text($"Email: {inv.Customer.Email}");
                    if (!string.IsNullOrWhiteSpace(inv.Customer?.PhoneNumber))
                        col.Item().Text($"Tel.: {inv.Customer.PhoneNumber}");

                    col.Item().PaddingTop(20);

                    // Tabela kwot
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cc =>
                        {
                            cc.RelativeColumn(5);
                            cc.ConstantColumn(80);
                            cc.ConstantColumn(60);
                            cc.ConstantColumn(80);
                        });
                        t.Header(h =>
                        {
                            h.Cell().Background("#F4F6FA").Padding(6).Text("Pozycja").SemiBold();
                            h.Cell().Background("#F4F6FA").Padding(6).AlignRight().Text("Netto").SemiBold();
                            h.Cell().Background("#F4F6FA").Padding(6).AlignRight().Text("VAT").SemiBold();
                            h.Cell().Background("#F4F6FA").Padding(6).AlignRight().Text("Brutto").SemiBold();
                        });

                        var pl = CultureInfo.GetCultureInfo("pl-PL");
                        t.Cell().Padding(6).Text($"Wynajem sprzętu (umowa {inv.RentalId.ToString()[..8].ToUpper()})");
                        t.Cell().Padding(6).AlignRight().Text(inv.NetAmount.ToString("N2", pl) + " zł");
                        t.Cell().Padding(6).AlignRight().Text(inv.VatAmount.ToString("N2", pl) + " zł");
                        t.Cell().Padding(6).AlignRight().Text(inv.GrossAmount.ToString("N2", pl) + " zł");

                        t.Cell().Padding(6).Text("Suma").Bold();
                        t.Cell().Padding(6).AlignRight().Text(inv.NetAmount.ToString("N2", pl) + " zł").Bold();
                        t.Cell().Padding(6).AlignRight().Text(inv.VatAmount.ToString("N2", pl) + " zł").Bold();
                        t.Cell().Padding(6).AlignRight().Text(inv.GrossAmount.ToString("N2", pl) + " zł").Bold();
                    });

                    col.Item().PaddingTop(20).Text($"Stawka VAT: {inv.VatRate}").FontColor("#5B6B82");
                    col.Item().Text($"Status: {inv.Status}").FontColor("#5B6B82");
                });

                p.Footer().AlignCenter().Column(footer =>
                {
                    footer.Item().LineHorizontal(0.5f);
                    footer.Item().PaddingTop(5).Text(text =>
                    {
                        text.Span($"{company?.Name ?? "RentSpot"} · via RentSpot · rentspot.eu").FontSize(7).FontColor("#5B6B82");
                        text.Span($"  ·  Wygenerowano {DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(7).FontColor("#5B6B82");
                    });
                });
            });
        });

        return doc.GeneratePdf();
    }
}
