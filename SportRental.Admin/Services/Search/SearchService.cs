using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Search;

public enum SearchHitType { Customer, Product, Rental }

public record SearchHit(
    SearchHitType Type,
    string Title,
    string Subtitle,
    string Url);

/// Lekkie globalne wyszukiwanie (Maciej: klient, produkt, umowa, telefon).
/// Wersja Sprint 2: Postgres ILIKE na kluczowych kolumnach. Pod skalę >10k
/// rekordów do upgrade na pg_trgm + GIN indexy lub tsvector materializowany.
public class SearchService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public SearchService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<SearchHit>> SearchAsync(
        Guid tenantId,
        string query,
        int max = 8,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new List<SearchHit>();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var pattern = $"%{query.Trim()}%";

        // Klienci — imię, email, telefon
        var customers = await db.Customers.AsNoTracking()
            .Where(c => EF.Functions.ILike(c.FullName, pattern)
                || (c.Email != null && EF.Functions.ILike(c.Email, pattern))
                || (c.PhoneNumber != null && EF.Functions.ILike(c.PhoneNumber, pattern)))
            .OrderBy(c => c.FullName)
            .Take(max)
            .Select(c => new SearchHit(
                SearchHitType.Customer,
                c.FullName,
                (c.PhoneNumber ?? c.Email) ?? "",
                $"/admin/customers?highlight={c.Id}"))
            .ToListAsync(ct);

        // Produkty — nazwa, SKU, kategoria
        var products = await db.Products.AsNoTracking()
            .Where(p => !p.IsDeleted
                && (EF.Functions.ILike(p.Name, pattern)
                    || EF.Functions.ILike(p.Sku, pattern)
                    || (p.Category != null && EF.Functions.ILike(p.Category, pattern))))
            .OrderBy(p => p.Name)
            .Take(max)
            .Select(p => new SearchHit(
                SearchHitType.Product,
                p.Name,
                $"{p.Sku} · {p.Category ?? "—"}",
                $"/admin/products?highlight={p.Id}"))
            .ToListAsync(ct);

        // Wynajmy — id (8 pierwszych znaków UUID), klient name
        // SQL: rental.Id::text ILIKE pattern AND join customer
        var rentals = await db.Rentals.AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => EF.Functions.ILike(r.Id.ToString(), pattern)
                || (r.Customer != null && EF.Functions.ILike(r.Customer.FullName, pattern)))
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(max)
            .Select(r => new SearchHit(
                SearchHitType.Rental,
                $"#{r.Id.ToString().Substring(0, 8).ToUpper()} · {(r.Customer != null ? r.Customer.FullName : "—")}",
                $"{r.StartDateUtc.ToLocalTime():dd.MM HH:mm} – {r.EndDateUtc.ToLocalTime():dd.MM HH:mm}",
                $"/admin/rentals?highlight={r.Id}"))
            .ToListAsync(ct);

        return customers.Concat(products).Concat(rentals).Take(max * 2).ToList();
    }
}
