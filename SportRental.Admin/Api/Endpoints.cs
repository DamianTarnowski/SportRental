using SportRental.Admin.Api.Models;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using SportRental.Admin.Services.Contracts;
using SportRental.Admin.Services.Sms;
using SportRental.Admin.Services.Storage;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Microsoft.AspNetCore.Identity;
using SharedModels = SportRental.Shared.Models;

namespace SportRental.Admin.Api
{
    public static class Endpoints
    {
        public static IEndpointRouteBuilder MapSportRentalApi(this IEndpointRouteBuilder app)
        {
            // Krótki link do umowy (poza /api żeby był krótszy)
            // Format: /c/{shortId} gdzie shortId to pierwsze 8 znaków GUID wynajmu
            app.MapGet("/c/{shortId}", [AllowAnonymous] async (
                string shortId,
                IDbContextFactory<ApplicationDbContext> dbFactory) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                
                // Szukaj wynajmu po pierwszych 8 znakach ID (bez filtra tenanta)
                var rental = await db.Rentals
                    .IgnoreQueryFilters()
                    .Include(r => r.Customer)
                    .Include(r => r.Items)
                    .Where(r => r.Id.ToString().StartsWith(shortId.ToLower()))
                    .FirstOrDefaultAsync();
                
                if (rental == null)
                {
                    return Results.NotFound("Umowa nie została znaleziona.");
                }
                
                // Jeśli jest URL do umowy PDF, przekieruj
                if (!string.IsNullOrWhiteSpace(rental.ContractUrl))
                {
                    return Results.Redirect(rental.ContractUrl);
                }
                
                // Jeśli nie ma PDF, pokaż podstawowe informacje jako HTML
                var html = $@"
<!DOCTYPE html>
<html lang=""pl"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Umowa wynajmu - SportRental</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; background: #f5f5f5; }}
        .card {{ background: white; border-radius: 12px; padding: 24px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }}
        h1 {{ color: #1976d2; font-size: 24px; margin-bottom: 8px; }}
        .info {{ margin: 16px 0; padding: 16px; background: #e3f2fd; border-radius: 8px; }}
        .label {{ color: #666; font-size: 14px; }}
        .value {{ font-size: 18px; font-weight: 600; color: #333; }}
        .status {{ display: inline-block; padding: 6px 12px; border-radius: 20px; font-size: 14px; font-weight: 600; }}
        .status.confirmed {{ background: #c8e6c9; color: #2e7d32; }}
        .status.pending {{ background: #fff3e0; color: #ef6c00; }}
        .footer {{ margin-top: 24px; text-align: center; color: #999; font-size: 12px; }}
    </style>
</head>
<body>
    <div class=""card"">
        <h1>🎿 SportRental</h1>
        <p>Szczegóły Twojego wynajmu</p>
        
        <div class=""info"">
            <div class=""label"">Klient</div>
            <div class=""value"">{rental.Customer?.FullName ?? "—"}</div>
        </div>
        
        <div class=""info"">
            <div class=""label"">Okres wynajmu</div>
            <div class=""value"">{rental.StartDateUtc:dd.MM.yyyy} - {rental.EndDateUtc:dd.MM.yyyy}</div>
        </div>
        
        <div class=""info"">
            <div class=""label"">Kwota</div>
            <div class=""value"">{rental.TotalAmount:N2} PLN + {rental.DepositAmount:N2} PLN kaucji</div>
        </div>
        
        <div class=""info"">
            <div class=""label"">Status</div>
            <span class=""status {(rental.IsSmsConfirmed ? "confirmed" : "pending")}"">
                {(rental.IsSmsConfirmed ? "✓ Potwierdzona" : "⏳ Oczekuje na potwierdzenie")}
            </span>
        </div>
        
        <div class=""footer"">
            <p>Aby potwierdzić umowę, odpisz <strong>TAK</strong> na otrzymanego SMS-a.</p>
            <p>ID: {rental.Id.ToString()[..8].ToUpper()}</p>
        </div>
    </div>
</body>
</html>";
                
                return Results.Content(html, "text/html; charset=utf-8");
            });
            
            var api = app.MapGroup("/api")
                .RequireCors(); // Enable CORS for all API endpoints
            
            // Auth endpoints
            MapAuthEndpoints(api);
            
            // Customer endpoints for WASM client
            MapCustomerEndpoints(api);

            api.MapGet("/products", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory, 
                ITenantProvider tenantProvider, 
                int? page, 
                int? pageSize,
                string? search,
                string? category,
                string? city,
                string? voivodeship,
                string? tenant,
                decimal? minPrice,
                decimal? maxPrice,
                bool? available,
                string? sort,
                double? userLat,
                double? userLon) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var tid = tenantProvider.GetCurrentTenantId();
                
                // Base query with tenant filter bypass
                var baseQuery = db.Products.IgnoreQueryFilters().AsNoTracking();
                
                if (tid.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.TenantId == tid.Value);
                }
                
                // Join with Tenants and CompanyInfos
                var query = baseQuery
                    .Join(db.Tenants, p => p.TenantId, t => t.Id, (p, t) => new { Product = p, Tenant = t })
                    .GroupJoin(db.CompanyInfos, x => x.Product.TenantId, ci => ci.TenantId, (x, cis) => new { x.Product, x.Tenant, CompanyInfo = cis.FirstOrDefault() })
                    .Select(x => new
                    {
                        Id = x.Product.Id,
                        TenantId = x.Product.TenantId,
                        TenantName = x.Tenant.Name,
                        Name = x.Product.Name,
                        Sku = x.Product.Sku,
                        Category = x.Product.Category,
                        Description = x.Product.Description,
                        ImageUrl = x.Product.ImageUrl,
                        PricePerDay = x.Product.DailyPrice,
                        DailyPrice = x.Product.DailyPrice,
                        HourlyPrice = x.Product.HourlyPrice,
                        Quantity = x.Product.AvailableQuantity,
                        AvailableQuantity = x.Product.AvailableQuantity,
                        IsAvailable = x.Product.Available && x.Product.IsActive && x.Product.AvailableQuantity > 0,
                        City = x.Product.City ?? x.CompanyInfo!.City,
                        Voivodeship = x.Product.Voivodeship ?? x.CompanyInfo!.Voivodeship,
                        Lat = x.CompanyInfo != null ? x.CompanyInfo.Lat : (double?)null,
                        Lon = x.CompanyInfo != null ? x.CompanyInfo.Lon : (double?)null
                    });

                // Apply filters
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchLower = search.ToLower();
                    query = query.Where(p => p.Name.ToLower().Contains(searchLower) || 
                                            (p.Description != null && p.Description.ToLower().Contains(searchLower)));
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    query = query.Where(p => p.Category != null && p.Category.ToLower() == category.ToLower());
                }

                if (!string.IsNullOrWhiteSpace(city))
                {
                    query = query.Where(p => p.City != null && p.City.ToLower() == city.ToLower());
                }

                if (!string.IsNullOrWhiteSpace(voivodeship))
                {
                    query = query.Where(p => p.Voivodeship != null && p.Voivodeship.ToLower() == voivodeship.ToLower());
                }

                if (!string.IsNullOrWhiteSpace(tenant))
                {
                    query = query.Where(p => p.TenantName.ToLower() == tenant.ToLower());
                }

                if (minPrice.HasValue)
                {
                    query = query.Where(p => p.DailyPrice >= minPrice.Value);
                }

                if (maxPrice.HasValue)
                {
                    query = query.Where(p => p.DailyPrice <= maxPrice.Value);
                }

                if (available == true)
                {
                    query = query.Where(p => p.IsAvailable);
                }

                // Get total count before pagination
                var totalCount = await query.CountAsync();

                // Apply sorting
                IOrderedQueryable<dynamic>? orderedQuery = sort?.ToLower() switch
                {
                    "price-asc" => query.OrderBy(p => p.DailyPrice),
                    "price-desc" => query.OrderByDescending(p => p.DailyPrice),
                    "name" => query.OrderBy(p => p.Name),
                    "distance" when userLat.HasValue && userLon.HasValue => 
                        query.OrderBy(p => p.Lat.HasValue && p.Lon.HasValue 
                            ? Math.Sqrt(Math.Pow((p.Lat.Value - userLat.Value) * 111.32, 2) + 
                                       Math.Pow((p.Lon.Value - userLon.Value) * 111.32 * Math.Cos(userLat.Value * Math.PI / 180), 2))
                            : 999999),
                    _ => query.OrderByDescending(p => p.IsAvailable).ThenBy(p => p.Name)
                };

                // Pagination
                var pageNum = Math.Max(1, page ?? 1);
                var pageSizeNum = Math.Clamp(pageSize ?? 12, 1, 100);
                
                var items = await orderedQuery!
                    .Skip((pageNum - 1) * pageSizeNum)
                    .Take(pageSizeNum)
                    .ToListAsync();

                return Results.Ok(new
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = pageNum,
                    PageSize = pageSizeNum,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSizeNum)
                });
            });

            // GET /api/products/{id} - pojedynczy produkt
            api.MapGet("/products/{id:guid}", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                Guid id,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var tid = tenantProvider.GetCurrentTenantId();

                var baseQuery = db.Products.IgnoreQueryFilters().AsNoTracking();
                if (tid.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.TenantId == tid.Value);
                }

                var product = await baseQuery
                    .Where(p => p.Id == id)
                    .Join(db.Tenants, p => p.TenantId, t => t.Id, (p, t) => new { Product = p, TenantName = t.Name })
                    .Select(x => new
                    {
                        Id = x.Product.Id,
                        TenantId = x.Product.TenantId,
                        TenantName = x.TenantName,
                        Name = x.Product.Name,
                        Sku = x.Product.Sku,
                        Category = x.Product.Category,
                        Description = x.Product.Description,
                        ImageUrl = x.Product.ImageUrl,
                        PricePerDay = x.Product.DailyPrice,
                        DailyPrice = x.Product.DailyPrice,
                        HourlyPrice = x.Product.HourlyPrice,
                        Quantity = x.Product.AvailableQuantity,
                        AvailableQuantity = x.Product.AvailableQuantity,
                        IsAvailable = x.Product.Available && x.Product.IsActive && x.Product.AvailableQuantity > 0,
                        City = x.Product.City,
                        Voivodeship = x.Product.Voivodeship
                    })
                    .FirstOrDefaultAsync(ct);

                return product is null ? Results.NotFound() : Results.Ok(product);
            });

            // GET /api/tenants/locations - lokalizacje wypożyczalni (dla mapy)
            api.MapGet("/tenants/locations", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var locations = await db.CompanyInfos
                    .AsNoTracking()
                    .Where(ci => ci.Lat.HasValue && ci.Lon.HasValue && ci.Lat != 0 && ci.Lon != 0)
                    .Join(db.Tenants, ci => ci.TenantId, t => t.Id, (ci, t) => new
                    {
                        TenantId = t.Id,
                        TenantName = t.Name,
                        Lat = ci.Lat,
                        Lon = ci.Lon,
                        Address = ci.Address,
                        City = ci.City,
                        Voivodeship = ci.Voivodeship,
                        PhoneNumber = ci.PhoneNumber,
                        Email = ci.Email,
                        OpeningHours = ci.OpeningHours,
                        LogoUrl = t.LogoUrl
                    })
                    .ToListAsync(ct);

                return Results.Ok(locations);
            });

            // POST /api/payments/quote - wycena płatności
            api.MapPost("/payments/quote", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                SharedModels.PaymentQuoteRequest req,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;

                try
                {
                    var computation = await Payments.PaymentCalculator.ComputeAsync(tenantId, req, db, ct);
                    return Results.Ok(new SharedModels.PaymentQuoteResponse
                    {
                        TotalAmount = computation.TotalAmount,
                        DepositAmount = computation.DepositAmount,
                        Currency = "PLN",
                        RentalDays = computation.RentalDays,
                        Tenants = computation.Tenants
                            .Select(t => new SharedModels.TenantQuoteBreakdown
                            {
                                TenantId = t.TenantId,
                                TotalAmount = t.TotalAmount,
                                DepositAmount = t.DepositAmount
                            })
                            .ToList()
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            // POST /api/payments/intents - tworzenie PaymentIntent (Stripe)
            api.MapPost("/payments/intents", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                Payments.IPaymentGateway gateway,
                SharedModels.CreatePaymentIntentRequest req,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;

                try
                {
                    var quoteRequest = new SharedModels.PaymentQuoteRequest
                    {
                        StartDateUtc = req.StartDateUtc,
                        EndDateUtc = req.EndDateUtc,
                        Items = req.Items
                    };

                    var computation = await Payments.PaymentCalculator.ComputeAsync(tenantId, quoteRequest, db, ct);
                    if (computation.Tenants.Count == 0)
                    {
                        return Results.BadRequest(new { error = "Brak pozycji do wyliczenia platnosci." });
                    }

                    var currency = string.IsNullOrWhiteSpace(req.Currency) ? "PLN" : req.Currency;
                    var tenantIds = computation.Tenants.Select(t => t.TenantId).Distinct().ToList();
                    var paymentTenant = tenantIds.Count == 1 ? tenantIds[0] : Guid.Empty;

                    var metadata = new Dictionary<string, string>
                    {
                        ["tenant_id"] = paymentTenant.ToString(),
                        ["tenant_ids"] = string.Join(",", tenantIds),
                        ["rental_start"] = req.StartDateUtc.ToString("O"),
                        ["rental_end"] = req.EndDateUtc.ToString("O"),
                        ["items_count"] = req.Items.Count.ToString(),
                        ["rental_days"] = computation.RentalDays.ToString(),
                        ["idempotency_key"] = $"pi:{Guid.NewGuid():N}"
                    };

                    var intent = await gateway.CreatePaymentIntentAsync(paymentTenant, computation.TotalAmount, computation.DepositAmount, currency, metadata);
                    return Results.Ok(intent);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            // GET /api/payments/intents/{id} - pobieranie PaymentIntent
            api.MapGet("/payments/intents/{id}", [AllowAnonymous] async (
                ITenantProvider tenantProvider,
                Payments.IPaymentGateway gateway,
                string id) =>
            {
                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                var intent = await gateway.GetPaymentIntentAsync(tenantId, id);
                return intent is null ? Results.NotFound() : Results.Ok(intent);
            });

            // Checkout endpoints for Stripe redirect flow
            MapCheckoutEndpoints(api);
            
            // SMS webhook endpoints for SerwerSMS.pl
            MapSmsEndpoints(api);

            api.MapPost("/rentals", [Authorize] async (CreateRentalRequest req, IDbContextFactory<ApplicationDbContext> dbFactory, ITenantProvider tenantProvider, IContractGenerator contracts, ISmsSender sms, IFileStorage storage) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var tid = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                db.SetTenant(tid);

                // Walidacja wejścia
                if (req == null)
                    return Results.BadRequest("Brak danych żądania");
                if (req.StartDateUtc >= req.EndDateUtc)
                    return Results.BadRequest("Data zakończenia musi być po dacie rozpoczęcia");
                if (req.Items == null || req.Items.Count == 0)
                    return Results.BadRequest("Brak pozycji wynajmu");
                if (req.Items.Any(i => i.Quantity <= 0))
                    return Results.BadRequest("Ilość w pozycji musi być większa od zera");
                var duplicates = req.Items
                    .GroupBy(i => i.ProductId)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();
                if (duplicates.Count > 0)
                    return Results.BadRequest($"Zduplikowane produkty w zamówieniu: {string.Join(", ", duplicates)}");

                // Weryfikacja istnienia klienta
                var customerExists = await db.Customers.AnyAsync(c => c.Id == req.CustomerId);
                if (!customerExists)
                    return Results.BadRequest("Nie znaleziono klienta");

                // Idempotency: if key provided and a rental exists, return it
                if (!string.IsNullOrWhiteSpace(req.IdempotencyKey))
                {
                    var existing = await db.Rentals
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.TenantId == tid && r.IdempotencyKey == req.IdempotencyKey);
                    if (existing != null)
                    {
                        return Results.Created($"/api/rentals/{existing.Id}", new { existing.Id, existing.TotalAmount, existing.ContractUrl });
                    }
                }

                var productIds = req.Items.Select(i => i.ProductId).ToList();
                var productMap = await db.Products
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p);
                if (productMap.Count != productIds.Count)
                {
                    var missing = productIds.Where(id => !productMap.ContainsKey(id));
                    return Results.BadRequest($"Nie znaleziono produktów: {string.Join(", ", missing)}");
                }

                var days = Math.Max(1, (int)Math.Ceiling((req.EndDateUtc - req.StartDateUtc).TotalDays));

                // Sekcja krytyczna: transakcja + izolacja Serializable
                await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
                {
                    var rental = new Rental
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tid,
                        CustomerId = req.CustomerId,
                        StartDateUtc = req.StartDateUtc,
                        EndDateUtc = req.EndDateUtc,
                        Status = RentalStatus.Confirmed,
                        Source = RentalSource.InStore, // Wypożyczenie fizyczne przez panel Admin
                        RentalType = (SportRental.Infrastructure.Domain.RentalType)(int)req.RentalType,
                        HoursRented = req.HoursRented,
                        CreatedAtUtc = DateTime.UtcNow,
                        IdempotencyKey = string.IsNullOrWhiteSpace(req.IdempotencyKey) ? null : req.IdempotencyKey
                    };

                    var items = new List<RentalItem>();
                    decimal total = 0;

                    foreach (var it in req.Items)
                    {
                        var product = productMap[it.ProductId];
                        var isHourly = req.RentalType == Models.RentalType.Hourly && product.HourlyPrice.HasValue && req.HoursRented.HasValue;
                        var price = isHourly ? product.HourlyPrice!.Value : product.DailyPrice;

                        // Ponowna walidacja dostępności w transakcji
                        var overlappingReservedQty = await db.RentalItems
                            .Where(ri => ri.ProductId == it.ProductId)
                            .Join(db.Rentals, ri => ri.RentalId, r => r.Id, (ri, r) => new { ri, r })
                            .Where(x => x.r.TenantId == tid
                                        && x.r.Status != RentalStatus.Cancelled
                                        && x.r.EndDateUtc > req.StartDateUtc
                                        && x.r.StartDateUtc < req.EndDateUtc)
                            .SumAsync(x => (int?)x.ri.Quantity) ?? 0;

                        // Aktywne holdy (nie wygasłe), które nakładają się terminem
                        var nowUtc = DateTime.UtcNow;
                        var activeHoldsQty = await db.ReservationHolds
                            .Where(h => h.ProductId == it.ProductId
                                        && h.TenantId == tid
                                        && h.ExpiresAtUtc > nowUtc
                                        && h.EndDateUtc > req.StartDateUtc
                                        && h.StartDateUtc < req.EndDateUtc)
                            .SumAsync(h => (int?)h.Quantity) ?? 0;

                        if (overlappingReservedQty + activeHoldsQty + it.Quantity > product.AvailableQuantity)
                            return Results.Conflict(new { message = $"Brak dostępności dla produktu {product.Name}. Dostępne: {Math.Max(0, product.AvailableQuantity - overlappingReservedQty)}", productId = product.Id });

                        var subtotal = isHourly 
                            ? price * it.Quantity * req.HoursRented!.Value 
                            : price * it.Quantity * days;
                        items.Add(new RentalItem
                        {
                            Id = Guid.NewGuid(),
                            RentalId = rental.Id,
                            ProductId = product.Id,
                            Quantity = it.Quantity,
                            PricePerDay = product.DailyPrice,
                            PricePerHour = product.HourlyPrice,
                            Subtotal = subtotal
                        });
                        total += subtotal;
                    }

                    rental.TotalAmount = total;
                    await db.Rentals.AddAsync(rental);
                    await db.RentalItems.AddRangeAsync(items);
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();

                    // Po commit: generowanie PDF i aktualizacja URL umowy (poza transakcją)
                    var customer = await db.Customers.FirstAsync(c => c.Id == rental.CustomerId);
                    var companyInfo = await db.CompanyInfos.FirstOrDefaultAsync(ci => ci.TenantId == rental.TenantId);
                    var template = await db.ContractTemplates.FirstOrDefaultAsync(ct => ct.TenantId == rental.TenantId);
                    
                    byte[] pdf = template == null
                        ? await contracts.GenerateRentalContractAsync(rental, items, customer, productMap.Values, companyInfo)
                        : await contracts.GenerateRentalContractAsync(template.Content, rental, items, customer, productMap.Values, companyInfo);
                    var relativePath = $"contracts/{rental.TenantId}/{rental.Id}.pdf";
                    var publicUrl = await storage.SaveAsync(relativePath, pdf);
                    rental.ContractUrl = publicUrl;
                    db.Rentals.Update(rental);
                    await db.SaveChangesAsync();

                    // Wysyłka powiadomień (w tle, nie blokuj response)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // SMS
                            if (!string.IsNullOrWhiteSpace(customer.PhoneNumber))
                            {
                                await sms.SendAsync(customer.PhoneNumber!, $"Potwierdzenie wynajmu {rental.Id}. Kwota: {rental.TotalAmount:0.00} zł");
                            }
                            
                            // Email z potwierdzeniem i umową PDF
                            if (!string.IsNullOrWhiteSpace(customer.Email))
                            {
                                await contracts.SendRentalConfirmationEmailAsync(rental, items, customer, productMap.Values, companyInfo);
                                rental.IsEmailSent = true;
                                // Note: nie zapisujemy tutaj do bazy bo to background task
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log error but don't fail the rental creation
                            Console.WriteLine($"Error sending notifications for rental {rental.Id}: {ex.Message}");
                        }
                    });

                    return Results.Created($"/api/rentals/{rental.Id}", new { rental.Id, rental.TotalAmount, rental.ContractUrl });
                }
            });

            api.MapGet("/contracts/{id:guid}", [Authorize] async (Guid id, IDbContextFactory<ApplicationDbContext> dbFactory) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == id);
                if (rental == null || string.IsNullOrWhiteSpace(rental.ContractUrl))
                    return Results.NotFound();
                return Results.Redirect(rental.ContractUrl);
            });

            // Upload zdjęcia produktu
            api.MapPost("/products/{id:guid}/image", [Authorize(Roles = "Owner")] async (Guid id, HttpRequest request, IDbContextFactory<ApplicationDbContext> dbFactory, ImageVariantService images, IConfiguration config) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
                if (product == null) return Results.NotFound();
                if (!request.HasFormContentType) return Results.BadRequest("Brak form-data");
                var form = await request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file == null || file.Length == 0) return Results.BadRequest("Brak pliku");
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                if (!allowed.Contains(ext)) return Results.BadRequest("Nieobsługiwane rozszerzenie pliku");
                var maxMb = config.GetValue<int?>("Storage:MaxUploadMB") ?? 5;
                if (file.Length > maxMb * 1024L * 1024L) return Results.BadRequest($"Plik jest zbyt duży. Maks: {maxMb} MB");
                await using var s = file.OpenReadStream();
                var (basePath, defaultUrl, variants) = await images.SaveProductImageAsync(product.TenantId, product.Id, file.FileName, s);
                product.ImageBasePath = basePath;
                product.ImageUrl = defaultUrl;
                db.Products.Update(product);
                await db.SaveChangesAsync();
                return Results.Ok(new { imageUrl = product.ImageUrl, basePath = product.ImageBasePath, variants });
            });

            // Upload logo tenanta
            api.MapPost("/tenants/{id:guid}/logo", [Authorize(Roles = "Owner")] async (Guid id, HttpRequest request, IDbContextFactory<ApplicationDbContext> dbFactory, IFileStorage storage, IConfiguration config) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                db.SetTenant(Guid.Empty);
                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
                if (tenant == null) return Results.NotFound();
                if (!request.HasFormContentType) return Results.BadRequest("Brak form-data");
                var form = await request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file == null || file.Length == 0) return Results.BadRequest("Brak pliku");
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".svg" };
                if (!allowed.Contains(ext)) return Results.BadRequest("Nieobsługiwane rozszerzenie pliku");
                var maxMb = config.GetValue<int?>("Storage:MaxUploadMB") ?? 5;
                if (file.Length > maxMb * 1024L * 1024L) return Results.BadRequest($"Plik jest zbyt duży. Maks: {maxMb} MB");
                var rel = $"images/tenants/{id}/{id}{ext}";
                await using var s = file.OpenReadStream();
                var url = await storage.SaveAsync(rel, s);
                tenant.LogoUrl = url;
                db.Tenants.Update(tenant);
                await db.SaveChangesAsync();
                return Results.Ok(new { logoUrl = url });
            });

            // Anulowanie (usunięcie logiczne) wynajmu
            api.MapDelete("/rentals/{id:guid}", [Authorize] async (Guid id, IDbContextFactory<ApplicationDbContext> dbFactory, ITenantProvider tenantProvider) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var tid = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                db.SetTenant(tid);

                var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == id);
                if (rental == null)
                    return Results.NotFound();

                if (rental.Status == RentalStatus.Cancelled)
                    return Results.Ok(new { id = rental.Id, status = rental.Status.ToString() });

                rental.Status = RentalStatus.Cancelled;
                db.Rentals.Update(rental);
                await db.SaveChangesAsync();
                return Results.Ok(new { id = rental.Id, status = rental.Status.ToString() });
            });

            // Lista wynajmów zalogowanego użytkownika/klienta (tenant scoped)
            api.MapGet("/my-rentals", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                string? status,
                DateTime? from,
                DateTime? to,
                Guid? customerId) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var tid = tenantProvider.GetCurrentTenantId();

                var query = db.Rentals
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(r => r.Items)
                        .ThenInclude(i => i.Product)
                    .Include(r => r.Customer)
                    .Where(r => tid == null || tid == Guid.Empty || r.TenantId == tid);

                // Filtruj po customerId jeśli podano
                if (customerId.HasValue)
                {
                    query = query.Where(r => r.CustomerId == customerId.Value);
                }

                if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RentalStatus>(status, true, out var st))
                {
                    query = query.Where(r => r.Status == st);
                }

                if (from.HasValue)
                {
                    query = query.Where(r => r.EndDateUtc >= from.Value);
                }
                if (to.HasValue)
                {
                    query = query.Where(r => r.StartDateUtc <= to.Value);
                }

                var list = await query
                    .OrderByDescending(r => r.StartDateUtc)
                    .Select(r => new SharedModels.MyRentalDto
                    {
                        Id = r.Id,
                        Title = r.Items.Count == 0
                            ? "Wynajem"
                            : (r.Items.Select(i => i.Product!.Name).FirstOrDefault() ?? "Wynajem") + (r.Items.Count > 1 ? $" (+{r.Items.Count - 1})" : string.Empty),
                        CustomerName = r.Customer != null ? r.Customer.FullName : string.Empty,
                        StartDateUtc = r.StartDateUtc,
                        EndDateUtc = r.EndDateUtc,
                        Quantity = r.Items.Sum(i => i.Quantity),
                        TotalAmount = r.TotalAmount,
                        DepositAmount = r.DepositAmount,
                        PaymentStatus = r.PaymentStatus,
                        Status = r.Status.ToString(),
                        CanCancel = r.Status != RentalStatus.Cancelled && r.StartDateUtc > DateTime.UtcNow,
                        ContractUrl = r.ContractUrl,
                        // Nowe pola do śledzenia wydania/zwrotu
                        IssuedAtUtc = r.IssuedAtUtc,
                        ReturnedAtUtc = r.ReturnedAtUtc,
                        IssueNotes = r.IssueNotes,
                        ReturnNotes = r.ReturnNotes,
                        ReturnDepositRefund = r.ReturnDepositRefund,
                        Items = r.Items.Select(i => new SharedModels.MyRentalItemDto
                        {
                            ProductId = i.ProductId,
                            ProductName = i.Product != null ? i.Product.Name : string.Empty,
                            Quantity = i.Quantity,
                            DailyPrice = i.PricePerDay,
                            TotalPrice = i.Subtotal
                        }).ToList()
                    })
                    .ToListAsync();

                return Results.Ok(list);
            });

            // Utworzenie krótkotrwałego holda na produkt
            api.MapPost("/holds", [AllowAnonymous] async (CreateHoldRequest req, IDbContextFactory<ApplicationDbContext> dbFactory, ITenantProvider tenantProvider) =>
            {
                if (req == null) return Results.BadRequest("Brak danych");
                if (req.Quantity <= 0) return Results.BadRequest("Ilość musi być > 0");
                if (req.StartDateUtc >= req.EndDateUtc) return Results.BadRequest("Zakres dat niepoprawny");

                await using var db = await dbFactory.CreateDbContextAsync();
                
                // Pobierz produkt (bez filtrowania po tenant - pokazujemy wszystkie)
                var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == req.ProductId);
                if (product == null) return Results.NotFound("Nie znaleziono produktu");

                // Tenant bierzemy Z PRODUKTU, nie z headera!
                var tid = product.TenantId;

                var ttl = Math.Clamp(req.TtlMinutes ?? 10, 5, 30);
                var nowUtc = DateTime.UtcNow;

                // policz zajętość: aktywne rezerwacje + aktywne holdy (IgnoreQueryFilters bo klient WASM nie ma tenant)
                var overlappingReservedQty = await db.RentalItems
                    .IgnoreQueryFilters()
                    .Where(ri => ri.ProductId == req.ProductId)
                    .Join(db.Rentals.IgnoreQueryFilters(), ri => ri.RentalId, r => r.Id, (ri, r) => new { ri, r })
                    .Where(x => x.r.TenantId == tid
                                && x.r.Status != RentalStatus.Cancelled
                                && x.r.EndDateUtc > req.StartDateUtc
                                && x.r.StartDateUtc < req.EndDateUtc)
                    .SumAsync(x => (int?)x.ri.Quantity) ?? 0;

                var activeHoldsQty = await db.ReservationHolds
                    .IgnoreQueryFilters()
                    .Where(h => h.ProductId == req.ProductId
                                && h.TenantId == tid
                                && h.ExpiresAtUtc > nowUtc
                                && h.EndDateUtc > req.StartDateUtc
                                && h.StartDateUtc < req.EndDateUtc)
                    .SumAsync(h => (int?)h.Quantity) ?? 0;

                if (overlappingReservedQty + activeHoldsQty + req.Quantity > product.AvailableQuantity)
                    return Results.Conflict(new { message = $"Brak dostępności. Dostępne: {Math.Max(0, product.AvailableQuantity - overlappingReservedQty - activeHoldsQty)}" });

                var hold = new ReservationHold
                {
                    Id = Guid.NewGuid(),
                    TenantId = tid, // tenant Z PRODUKTU
                    ProductId = req.ProductId,
                    Quantity = req.Quantity,
                    StartDateUtc = req.StartDateUtc,
                    EndDateUtc = req.EndDateUtc,
                    CreatedAtUtc = nowUtc,
                    ExpiresAtUtc = nowUtc.AddMinutes(ttl),
                    CustomerId = req.CustomerId,
                    SessionId = req.SessionId
                };

                await db.ReservationHolds.AddAsync(hold);
                await db.SaveChangesAsync();
                return Results.Created($"/api/holds/{hold.Id}", new { hold.Id, hold.ExpiresAtUtc });
            });

            // Usunięcie (zwolnienie) holda
            api.MapDelete("/holds/{id:guid}", [AllowAnonymous] async (Guid id, IDbContextFactory<ApplicationDbContext> dbFactory, ITenantProvider tenantProvider) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                // IgnoreQueryFilters bo klient WASM nie ma tenant
                var hold = await db.ReservationHolds.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == id);
                if (hold == null) return Results.NotFound();
                db.ReservationHolds.Remove(hold);
                await db.SaveChangesAsync();
                return Results.Ok();
            });

            return app;
        }

        private static void MapAuthEndpoints(RouteGroupBuilder api)
        {
            var auth = api.MapGroup("/auth");

            // Register endpoint (cookie-based, no JWT needed)
            auth.MapPost("/register", [AllowAnonymous] async (
                RegisterRequest request,
                UserManager<ApplicationUser> userManager,
                SignInManager<ApplicationUser> signInManager,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                HttpContext httpContext) =>
            {
                if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.BadRequest(new { error = "Email i hasło są wymagane" });
                }

                // Get tenant from header
                var tenantIdHeader = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(tenantIdHeader) || !Guid.TryParse(tenantIdHeader, out var tenantId))
                {
                    return Results.BadRequest(new { error = "Header X-Tenant-Id jest wymagany" });
                }

                // Verify tenant exists
                await using var db = await dbFactory.CreateDbContextAsync();
                var tenantExists = await db.Tenants.AnyAsync(t => t.Id == tenantId);
                if (!tenantExists)
                {
                    return Results.BadRequest(new { error = "Nieprawidłowy Tenant ID" });
                }

                // Check if email already exists
                var existingUser = await userManager.FindByEmailAsync(request.Email);
                if (existingUser != null)
                {
                    return Results.BadRequest(new { error = "Email już jest zarejestrowany" });
                }

                var user = new ApplicationUser
                {
                    UserName = request.Email,
                    Email = request.Email,
                    TenantId = tenantId,
                    EmailConfirmed = true // Auto-confirm in development
                };

                var result = await userManager.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    return Results.BadRequest(new { error = errors });
                }

                // Assign Client role
                await userManager.AddToRoleAsync(user, "Client");

                // Automatically create Customer record
                var customer = new Customer
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    FullName = request.FullName ?? request.Email.Split('@')[0],
                    Email = request.Email,
                    PhoneNumber = request.PhoneNumber,
                    DocumentNumber = request.DocumentNumber,
                    CreatedAtUtc = DateTime.UtcNow
                };

                db.Customers.Add(customer);
                await db.SaveChangesAsync();

                // Sign in the user (cookie-based)
                await signInManager.SignInAsync(user, isPersistent: false);

                // Return response (mock JWT format for compatibility)
                return Results.Ok(new
                {
                    AccessToken = "cookie-based-auth",
                    RefreshToken = "not-used",
                    ExpiresIn = 3600,
                    TokenType = "Cookie",
                    User = new
                    {
                        Id = user.Id,
                        Email = user.Email,
                        TenantId = tenantId
                    }
                });
            });

            // Login endpoint
            auth.MapPost("/login", [AllowAnonymous] async (
                LoginRequest request,
                UserManager<ApplicationUser> userManager,
                SignInManager<ApplicationUser> signInManager,
                HttpContext httpContext) =>
            {
                if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.BadRequest(new { error = "Email i hasło są wymagane" });
                }

                var user = await userManager.FindByEmailAsync(request.Email);
                if (user == null)
                {
                    return Results.BadRequest(new { error = "Nieprawidłowy email lub hasło" });
                }

                var result = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
                if (!result.Succeeded)
                {
                    if (result.IsLockedOut)
                        return Results.BadRequest(new { error = "Konto zablokowane" });
                    
                    return Results.BadRequest(new { error = "Nieprawidłowy email lub hasło" });
                }

                return Results.Ok(new
                {
                    AccessToken = "cookie-based-auth",
                    RefreshToken = "not-used",
                    ExpiresIn = 3600,
                    TokenType = "Cookie",
                    User = new
                    {
                        Id = user.Id,
                        Email = user.Email,
                        TenantId = user.TenantId
                    }
                });
            });
        }

        // Customer endpoints for WASM client
        private static void MapCustomerEndpoints(IEndpointRouteBuilder api)
        {
            // GET /api/customers/by-email?email=xxx
            api.MapGet("/customers/by-email", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory, 
                ITenantProvider tenantProvider,
                string? email,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(email))
                {
                    return Results.BadRequest(new { error = "Email query parameter is required." });
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                var normalizedEmail = email.Trim().ToLower();

                var query = db.Customers.IgnoreQueryFilters()
                    .Where(c => c.Email != null && c.Email.ToLower() == normalizedEmail);
                
                if (tenantId != Guid.Empty)
                {
                    query = query.Where(c => c.TenantId == tenantId);
                }

                var customer = await query.FirstOrDefaultAsync(ct);
                return customer is null 
                    ? Results.NotFound() 
                    : Results.Ok(ToCustomerDto(customer));
            });

            // GET /api/customers/{id}
            api.MapGet("/customers/{id:guid}", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                Guid id,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;

                var query = db.Customers.IgnoreQueryFilters().Where(c => c.Id == id);
                if (tenantId != Guid.Empty)
                {
                    query = query.Where(c => c.TenantId == tenantId);
                }

                var customer = await query.FirstOrDefaultAsync(ct);
                return customer is null 
                    ? Results.NotFound() 
                    : Results.Ok(ToCustomerDto(customer));
            });

            // POST /api/customers
            api.MapPost("/customers", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                SharedModels.CreateCustomerRequest req,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                var normalizedEmail = req.Email?.Trim();

                if (!string.IsNullOrEmpty(normalizedEmail))
                {
                    var query = db.Customers.IgnoreQueryFilters()
                        .Where(c => c.Email != null && c.Email.ToLower() == normalizedEmail.ToLower());
                    
                    if (tenantId != Guid.Empty)
                    {
                        query = query.Where(c => c.TenantId == tenantId);
                    }

                    var existingCustomer = await query.FirstOrDefaultAsync(ct);
                    if (existingCustomer is not null)
                    {
                        // Return existing customer for WASM client convenience
                        if (tenantId == Guid.Empty)
                        {
                            return Results.Ok(ToCustomerDto(existingCustomer));
                        }
                        return Results.Conflict(new { error = "Customer with the provided email already exists." });
                    }
                }

                var customer = new Customer
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    FullName = req.FullName,
                    Email = normalizedEmail,
                    PhoneNumber = req.PhoneNumber?.Trim(),
                    Address = req.Address,
                    DocumentNumber = req.DocumentNumber,
                    Notes = req.Notes,
                    CreatedAtUtc = DateTime.UtcNow
                };

                await db.Customers.AddAsync(customer, ct);
                await db.SaveChangesAsync(ct);

                return Results.Created($"/api/customers/{customer.Id}", ToCustomerDto(customer));
            });

            // PUT /api/customers/{id}
            api.MapPut("/customers/{id:guid}", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                Guid id,
                SharedModels.CreateCustomerRequest req,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;

                var customer = await db.Customers.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == id, ct);

                if (customer is null)
                {
                    return Results.NotFound();
                }

                if (tenantId != Guid.Empty && customer.TenantId != Guid.Empty && customer.TenantId != tenantId)
                {
                    return Results.NotFound();
                }

                var normalizedEmail = req.Email?.Trim();
                var emailChanged = !string.Equals(customer.Email?.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase);
                
                if (!string.IsNullOrEmpty(normalizedEmail) && emailChanged)
                {
                    var conflictQuery = db.Customers.IgnoreQueryFilters()
                        .Where(c => c.Id != id && c.Email != null && c.Email.ToLower() == normalizedEmail.ToLower());
                    
                    if (tenantId != Guid.Empty)
                    {
                        conflictQuery = conflictQuery.Where(c => c.TenantId == tenantId);
                    }

                    if (await conflictQuery.AnyAsync(ct))
                    {
                        return Results.Conflict(new { error = "Customer with the provided email already exists." });
                    }
                }

                customer.FullName = req.FullName;
                customer.Email = normalizedEmail;
                customer.PhoneNumber = req.PhoneNumber?.Trim();
                customer.Address = req.Address;
                customer.DocumentNumber = req.DocumentNumber;
                customer.Notes = req.Notes;

                await db.SaveChangesAsync(ct);

                return Results.Ok(ToCustomerDto(customer));
            });
        }

        private static SharedModels.CustomerDto ToCustomerDto(Customer c) => new()
        {
            Id = c.Id,
            FullName = c.FullName,
            Email = c.Email ?? string.Empty,
            PhoneNumber = c.PhoneNumber ?? string.Empty,
            Address = c.Address,
            DocumentNumber = c.DocumentNumber,
            Notes = c.Notes
        };

        // Checkout endpoints for Stripe redirect flow
        private static void MapCheckoutEndpoints(IEndpointRouteBuilder api)
        {
            var checkout = api.MapGroup("/checkout");

            // POST /api/checkout/create-session
            checkout.MapPost("/create-session", [AllowAnonymous] async (
                HttpRequest httpRequest,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                IConfiguration configuration,
                Microsoft.Extensions.Options.IOptions<Payments.StripeOptions> stripeOptions,
                SharedModels.CreateCheckoutSessionRequest request,
                CancellationToken ct) =>
            {
                if (request.StartDateUtc >= request.EndDateUtc)
                {
                    return Results.BadRequest(new { error = "Start date must be before end date." });
                }

                if (request.Items == null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { error = "At least one item is required." });
                }

                if (!request.CustomerId.HasValue)
                {
                    return Results.BadRequest(new { error = "CustomerId is required for checkout." });
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                try
                {
                    var computation = await Payments.PaymentCalculator.ComputeAsync(
                        Guid.Empty,
                        new SharedModels.PaymentQuoteRequest
                        {
                            StartDateUtc = request.StartDateUtc,
                            EndDateUtc = request.EndDateUtc,
                            Items = request.Items
                                .Select(i => new SharedModels.CreateRentalItem { ProductId = i.ProductId, Quantity = i.Quantity })
                                .ToList()
                        },
                        db,
                        ct);

                    var customer = await db.Customers
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Id == request.CustomerId.Value, ct);

                    if (customer is null)
                    {
                        return Results.BadRequest(new { error = "Customer not found." });
                    }

                    var stripe = stripeOptions.Value;
                    if (string.IsNullOrWhiteSpace(stripe.SecretKey))
                    {
                        return Results.BadRequest(new { error = "Stripe is not configured." });
                    }

                    Stripe.StripeConfiguration.ApiKey = stripe.SecretKey;

                    // Automatyczne wykrywanie URL klienta na podstawie środowiska
                    var isDevelopment = configuration["ASPNETCORE_ENVIRONMENT"] == "Development" 
                        || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
                    var clientBaseUrl = isDevelopment 
                        ? "http://localhost:5014" 
                        : "https://nice-tree-0359d8403.3.azurestaticapps.net";
                    
                    var successUrl = stripe.SuccessUrl ?? configuration["Stripe:SuccessUrl"] ?? $"{clientBaseUrl}/checkout/success";
                    var cancelUrl = stripe.CancelUrl ?? configuration["Stripe:CancelUrl"] ?? $"{clientBaseUrl}/checkout/cancel";

                    var idempotencyKey = $"checkout:{Guid.NewGuid():N}";
                    var depositAmount = computation.DepositAmount <= 0 ? computation.TotalAmount : computation.DepositAmount;
                    var depositUnitAmount = Math.Max(1, (long)Math.Round(depositAmount * 100, MidpointRounding.AwayFromZero));

                    var checkoutPayload = new Payments.CheckoutRentalPayload
                    {
                        Customer = new Payments.CheckoutCustomerSnapshot
                        {
                            CustomerId = customer.Id,
                            FullName = customer.FullName,
                            Email = customer.Email,
                            PhoneNumber = customer.PhoneNumber,
                            Address = customer.Address,
                            DocumentNumber = customer.DocumentNumber,
                            Notes = customer.Notes
                        },
                        StartDateUtc = request.StartDateUtc,
                        EndDateUtc = request.EndDateUtc,
                        Tenants = computation.Tenants
                            .Select(t => new Payments.CheckoutTenantPayload
                            {
                                TenantId = t.TenantId,
                                Items = t.Items.ToList(),
                                TotalAmount = t.TotalAmount,
                                DepositAmount = t.DepositAmount
                            })
                            .ToList(),
                        Notes = null,
                        IdempotencyKey = idempotencyKey,
                        TotalAmount = computation.TotalAmount,
                        DepositAmount = depositAmount
                    };

                    if (checkoutPayload.Tenants.Count == 0)
                    {
                        return Results.BadRequest(new { error = "Brak pozycji do finalizacji." });
                    }

                    var tenantIds = checkoutPayload.Tenants.Select(t => t.TenantId).Distinct().ToList();
                    
                    // Zapisz payload w bazie danych zamiast w metadata Stripe (limit 500 znaków)
                    var payloadJson = System.Text.Json.JsonSerializer.Serialize(checkoutPayload);
                    var checkoutSession = new Infrastructure.Domain.CheckoutSession
                    {
                        Id = Guid.NewGuid(),
                        IdempotencyKey = idempotencyKey,
                        PayloadJson = payloadJson,
                        CreatedAtUtc = DateTime.UtcNow,
                        ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
                    };
                    db.CheckoutSessions.Add(checkoutSession);
                    await db.SaveChangesAsync(ct);

                    var metadata = new Dictionary<string, string>
                    {
                        ["tenant_ids"] = string.Join(",", tenantIds),
                        ["customer_id"] = customer.Id.ToString(),
                        ["rental_start"] = request.StartDateUtc.ToString("O"),
                        ["rental_end"] = request.EndDateUtc.ToString("O"),
                        ["items_count"] = request.Items.Count.ToString(),
                        ["idempotency_key"] = idempotencyKey,
                        ["checkout_session_id"] = checkoutSession.Id.ToString()
                    };

                    var customerEmail = string.IsNullOrWhiteSpace(request.CustomerEmail)
                        ? customer.Email
                        : request.CustomerEmail;

                    var sessionService = new Stripe.Checkout.SessionService();
                    var sessionOptions = new Stripe.Checkout.SessionCreateOptions
                    {
                        SuccessUrl = successUrl + "?session_id={CHECKOUT_SESSION_ID}",
                        CancelUrl = cancelUrl,
                        Mode = "payment",
                        CustomerEmail = customerEmail,
                        ExpiresAt = DateTime.UtcNow.AddHours(23),
                        PaymentIntentData = new Stripe.Checkout.SessionPaymentIntentDataOptions
                        {
                            Metadata = metadata,
                            CaptureMethod = "automatic"
                        },
                        LineItems = new List<Stripe.Checkout.SessionLineItemOptions>
                        {
                            new()
                            {
                                PriceData = new Stripe.Checkout.SessionLineItemPriceDataOptions
                                {
                                    Currency = "pln",
                                    UnitAmount = depositUnitAmount,
                                    ProductData = new Stripe.Checkout.SessionLineItemPriceDataProductDataOptions
                                    {
                                        Name = "Depozyt za wypożyczenie sprzętu",
                                        Description = $"Okres: {request.StartDateUtc:d} - {request.EndDateUtc:d}"
                                    }
                                },
                                Quantity = 1
                            }
                        },
                        Metadata = metadata
                    };

                    var session = await sessionService.CreateAsync(sessionOptions, cancellationToken: ct);

                    return Results.Ok(new SharedModels.CheckoutSessionResponse(
                        session.Id,
                        session.Url ?? "",
                        session.ExpiresAt));
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"❌ Checkout create-session InvalidOperationException: {ex.Message}");
                    return Results.BadRequest(new { error = ex.Message });
                }
                catch (Stripe.StripeException ex)
                {
                    Console.WriteLine($"❌ Checkout create-session StripeException: {ex.Message}");
                    return Results.BadRequest(new { error = $"Stripe error: {ex.Message}" });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Checkout create-session Exception: {ex.GetType().Name}: {ex.Message}");
                    return Results.BadRequest(new { error = $"Unexpected error: {ex.Message}" });
                }
            });

            // POST /api/checkout/finalize-session/{sessionId}
            checkout.MapPost("/finalize-session/{sessionId}", [AllowAnonymous] async (
                string sessionId,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                IContractGenerator contracts,
                ISmsSender sms,
                IFileStorage storage,
                Microsoft.Extensions.Options.IOptions<Payments.StripeOptions> stripeOptions,
                CancellationToken ct) =>
            {
                var stripe = stripeOptions.Value;
                if (string.IsNullOrWhiteSpace(stripe.SecretKey))
                {
                    return Results.BadRequest(new { error = "Stripe is not configured." });
                }

                Stripe.StripeConfiguration.ApiKey = stripe.SecretKey;

                try
                {
                    var sessionService = new Stripe.Checkout.SessionService();
                    var session = await sessionService.GetAsync(sessionId, cancellationToken: ct);

                    if (session.PaymentStatus != "paid")
                    {
                        return Results.Ok(new SharedModels.FinalizeSessionResponse(
                            false,
                            $"Payment not completed. Status: {session.PaymentStatus}",
                            null));
                    }

                    // Check if rental already created (idempotency)
                    if (session.Metadata.TryGetValue("idempotency_key", out var idempotencyKey))
                    {
                        await using var checkDb = await dbFactory.CreateDbContextAsync(ct);
                        var existingRental = await checkDb.Rentals
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);

                        if (existingRental != null)
                        {
                            return Results.Ok(new SharedModels.FinalizeSessionResponse(
                                true,
                                "Rental already created.",
                                existingRental.Id));
                        }
                    }

                    // Get checkout payload from database
                    if (!session.Metadata.TryGetValue("checkout_session_id", out var checkoutSessionIdStr) 
                        || !Guid.TryParse(checkoutSessionIdStr, out var checkoutSessionId))
                    {
                        return Results.BadRequest(new { error = "Missing checkout session ID in session metadata." });
                    }

                    await using var payloadDb = await dbFactory.CreateDbContextAsync(ct);
                    var checkoutSession = await payloadDb.CheckoutSessions
                        .FirstOrDefaultAsync(cs => cs.Id == checkoutSessionId, ct);

                    if (checkoutSession == null)
                    {
                        return Results.BadRequest(new { error = "Checkout session not found." });
                    }

                    var payload = System.Text.Json.JsonSerializer.Deserialize<Payments.CheckoutRentalPayload>(checkoutSession.PayloadJson);

                    if (payload == null)
                    {
                        return Results.BadRequest(new { error = "Invalid checkout payload." });
                    }
                    
                    // Mark checkout session as processed
                    checkoutSession.IsProcessed = true;
                    checkoutSession.StripeSessionId = sessionId;
                    await payloadDb.SaveChangesAsync(ct);

                    // Create rental for each tenant
                    Guid? firstRentalId = null;
                    await using var db = await dbFactory.CreateDbContextAsync(ct);

                    foreach (var tenantPayload in payload.Tenants)
                    {
                        db.SetTenant(tenantPayload.TenantId);

                        var rental = new Rental
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantPayload.TenantId,
                            CustomerId = payload.Customer.CustomerId,
                            StartDateUtc = payload.StartDateUtc,
                            EndDateUtc = payload.EndDateUtc,
                            TotalAmount = tenantPayload.TotalAmount,
                            DepositAmount = tenantPayload.DepositAmount,
                            Status = RentalStatus.Confirmed,
                            PaymentStatus = "DepositPaid",
                            PaymentIntentId = session.PaymentIntentId,
                            IdempotencyKey = payload.IdempotencyKey,
                            Notes = payload.Notes,
                            Source = RentalSource.Online, // Wypożyczenie online z WASM
                            CreatedAtUtc = DateTime.UtcNow
                        };

                        foreach (var item in tenantPayload.Items)
                        {
                            var product = await db.Products
                                .IgnoreQueryFilters()
                                .FirstOrDefaultAsync(p => p.Id == item.ProductId, ct);

                            if (product != null)
                            {
                                rental.Items.Add(new RentalItem
                                {
                                    Id = Guid.NewGuid(),
                                    ProductId = item.ProductId,
                                    Quantity = item.Quantity,
                                    PricePerDay = product.DailyPrice
                                });
                            }
                        }

                        db.Rentals.Add(rental);
                        await db.SaveChangesAsync(ct);

                        firstRentalId ??= rental.Id;
                        
                        // Automatyczne generowanie umowy i wysyłanie emaila
                        try
                        {
                            var customer = await db.Customers
                                .IgnoreQueryFilters()
                                .FirstOrDefaultAsync(c => c.Id == rental.CustomerId, ct);
                            
                            var productIds = rental.Items.Select(i => i.ProductId).ToList();
                            var products = await db.Products
                                .IgnoreQueryFilters()
                                .Where(p => productIds.Contains(p.Id))
                                .ToListAsync(ct);
                            
                            var companyInfo = await db.CompanyInfos
                                .IgnoreQueryFilters()
                                .FirstOrDefaultAsync(ci => ci.TenantId == tenantPayload.TenantId, ct);
                            
                            if (customer != null)
                            {
                                // Generuj umowę i zapisz URL
                                var contractUrl = await contracts.GenerateAndSaveRentalContractAsync(
                                    rental, rental.Items, customer, products, companyInfo, ct);
                                
                                rental.ContractUrl = contractUrl;
                                await db.SaveChangesAsync(ct);
                                
                                // Wyślij email z umową jeśli klient ma email
                                if (!string.IsNullOrWhiteSpace(customer.Email))
                                {
                                    await contracts.SendRentalConfirmationEmailAsync(
                                        rental, rental.Items, customer, products, companyInfo, ct);
                                    
                                    rental.IsEmailSent = true;
                                    await db.SaveChangesAsync(ct);
                                    
                                    Console.WriteLine($"✅ Email z umową wysłany do {customer.Email} dla wynajmu {rental.Id}");
                                }
                            }
                        }
                        catch (Exception contractEx)
                        {
                            // Nie przerywaj procesu jeśli generowanie umowy/email się nie powiedzie
                            Console.WriteLine($"⚠️ Błąd generowania umowy/emaila: {contractEx.Message}");
                        }
                    }

                    return Results.Ok(new SharedModels.FinalizeSessionResponse(
                        true,
                        "Rental created successfully.",
                        firstRentalId));
                }
                catch (Stripe.StripeException ex)
                {
                    return Results.BadRequest(new { error = $"Stripe error: {ex.Message}" });
                }
            });
        }
        
        // SMS webhook endpoints for SerwerSMS.pl
        private static void MapSmsEndpoints(IEndpointRouteBuilder api)
        {
            var sms = api.MapGroup("/sms");
            
            // Webhook do odbierania przychodzących SMS z SerwerSMS.pl
            // URL do ustawienia w panelu SerwerSMS: https://sradmin2.azurewebsites.net/api/sms/incoming
            // Format: ?wiadomosc=#WIADOMOSC#&numer=#NUMER#&data=#DATA#&id=#ID#
            sms.MapGet("/incoming", [AllowAnonymous] async (
                ISmsConfirmationService confirmationService,
                ILoggerFactory loggerFactory,
                string? wiadomosc,
                string? numer,
                string? data,
                string? id,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("SmsWebhook");
                logger.LogInformation("Incoming SMS webhook: numer={Numer}, wiadomosc={Wiadomosc}, data={Data}, id={Id}", 
                    numer, wiadomosc, data, id);
                
                if (string.IsNullOrWhiteSpace(numer) || string.IsNullOrWhiteSpace(wiadomosc))
                {
                    return Results.Text("OK"); // SerwerSMS wymaga odpowiedzi OK
                }
                
                try
                {
                    var result = await confirmationService.ProcessIncomingSmsAsync(numer, wiadomosc, id, ct);
                    logger.LogInformation("SMS processed: IsProcessed={IsProcessed}, IsConfirmation={IsConfirmation}, RentalId={RentalId}", 
                        result.IsProcessed, result.IsConfirmation, result.RentalId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing incoming SMS from {Numer}", numer);
                }
                
                return Results.Text("OK"); // SerwerSMS wymaga odpowiedzi OK
            });
            
            // Alternatywny endpoint POST dla większej elastyczności
            sms.MapPost("/incoming", [AllowAnonymous] async (
                ISmsConfirmationService confirmationService,
                ILoggerFactory loggerFactory,
                HttpRequest request,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("SmsWebhook");
                var form = await request.ReadFormAsync(ct);
                var numer = form["phone"].FirstOrDefault() ?? form["numer"].FirstOrDefault();
                var wiadomosc = form["text"].FirstOrDefault() ?? form["wiadomosc"].FirstOrDefault() ?? form["message"].FirstOrDefault();
                var id = form["id"].FirstOrDefault() ?? form["message_id"].FirstOrDefault();
                
                logger.LogInformation("Incoming SMS POST webhook: numer={Numer}, wiadomosc={Wiadomosc}, id={Id}", 
                    numer, wiadomosc, id);
                
                if (string.IsNullOrWhiteSpace(numer) || string.IsNullOrWhiteSpace(wiadomosc))
                {
                    return Results.Text("OK");
                }
                
                try
                {
                    var result = await confirmationService.ProcessIncomingSmsAsync(numer, wiadomosc, id, ct);
                    logger.LogInformation("SMS processed: IsProcessed={IsProcessed}, IsConfirmation={IsConfirmation}, RentalId={RentalId}", 
                        result.IsProcessed, result.IsConfirmation, result.RentalId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing incoming SMS from {Numer}", numer);
                }
                
                return Results.Text("OK");
            });
            
            // Endpoint do wysyłania SMS z prośbą o potwierdzenie umowy (dla panelu admina)
            sms.MapPost("/send-confirmation/{rentalId:guid}", [Authorize] async (
                Guid rentalId,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                ISmsConfirmationService confirmationService,
                ISmsSender smsSender,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("SmsWebhook");
                var tenantId = tenantProvider.GetCurrentTenantId();
                if (tenantId == null)
                    return Results.Unauthorized();
                
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                db.SetTenant(tenantId);
                
                var rental = await db.Rentals
                    .Include(r => r.Customer)
                    .FirstOrDefaultAsync(r => r.Id == rentalId, ct);
                
                if (rental == null)
                    return Results.NotFound(new { error = "Wynajem nie znaleziony" });
                
                if (rental.Customer == null || string.IsNullOrWhiteSpace(rental.Customer.PhoneNumber))
                    return Results.BadRequest(new { error = "Klient nie ma numeru telefonu" });
                
                if (rental.IsSmsConfirmed)
                    return Results.BadRequest(new { error = "Umowa już została potwierdzona przez SMS" });
                
                try
                {
                    // Wygeneruj kod potwierdzenia (zapisuje do bazy)
                    await confirmationService.GenerateConfirmationCodeAsync(rentalId, ct);
                    
                    // Wyślij SMS z prośbą o potwierdzenie (z emailem klienta)
                    await smsSender.SendContractConfirmationRequestAsync(
                        rental.Customer.PhoneNumber, 
                        rental.Customer.FullName ?? "Kliencie", 
                        rentalId,
                        rental.Customer.Email,
                        ct);
                    
                    logger.LogInformation("Sent contract confirmation SMS for rental {RentalId} to {Phone}", 
                        rentalId, rental.Customer.PhoneNumber);
                    
                    return Results.Ok(new { 
                        success = true, 
                        message = $"SMS z prośbą o potwierdzenie wysłany do {rental.Customer.PhoneNumber}" 
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send confirmation SMS for rental {RentalId}", rentalId);
                    return Results.BadRequest(new { error = $"Błąd wysyłania SMS: {ex.Message}" });
                }
            });
        }
    }

    // DTOs for auth endpoints
    public record RegisterRequest(string Email, string Password, string? FullName, string? PhoneNumber, string? DocumentNumber);
    public record LoginRequest(string Email, string Password);
}

