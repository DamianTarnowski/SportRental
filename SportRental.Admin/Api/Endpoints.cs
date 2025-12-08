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

namespace SportRental.Admin.Api
{
    public static class Endpoints
    {
        public static IEndpointRouteBuilder MapSportRentalApi(this IEndpointRouteBuilder app)
        {
            var api = app.MapGroup("/api")
                .RequireCors(); // Enable CORS for all API endpoints
            
            // Auth endpoints
            MapAuthEndpoints(api);

            api.MapGet("/products", [AllowAnonymous] async (IDbContextFactory<ApplicationDbContext> dbFactory, ITenantProvider tenantProvider, int? page, int? pageSize) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var tid = tenantProvider.GetCurrentTenantId();
                
                // Użyj IgnoreQueryFilters żeby pominąć globalny filtr tenanta
                // Następnie ręcznie filtruj po tenancie jeśli podany
                var baseQuery = db.Products.IgnoreQueryFilters().AsNoTracking();
                
                if (tid.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.TenantId == tid.Value);
                }
                
                var query = baseQuery.Select(p => new
                    {
                        Id = p.Id,
                        TenantId = p.TenantId,
                        Name = p.Name,
                        Sku = p.Sku,
                        Category = p.Category,
                        Description = p.Description,
                        ImageUrl = p.ImageUrl,
                        PricePerDay = p.DailyPrice,
                        DailyPrice = p.DailyPrice,
                        Quantity = p.AvailableQuantity,
                        AvailableQuantity = p.AvailableQuantity,
                        IsAvailable = p.Available && p.IsActive && p.AvailableQuantity > 0
                    });

                var p = Math.Max(1, page ?? 1);
                var ps = Math.Clamp(pageSize ?? 50, 1, 200);
                var products = await query
                    .OrderBy(p => p.Name)
                    .Skip((p - 1) * ps)
                    .Take(ps)
                    .ToListAsync();
                return Results.Ok(products);
            });

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
                        CreatedAtUtc = DateTime.UtcNow,
                        IdempotencyKey = string.IsNullOrWhiteSpace(req.IdempotencyKey) ? null : req.IdempotencyKey
                    };

                    var items = new List<RentalItem>();
                    decimal total = 0;

                    foreach (var it in req.Items)
                    {
                        var product = productMap[it.ProductId];
                        var price = product.DailyPrice;

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

                        var subtotal = price * it.Quantity * days;
                        items.Add(new RentalItem
                        {
                            Id = Guid.NewGuid(),
                            RentalId = rental.Id,
                            ProductId = product.Id,
                            Quantity = it.Quantity,
                            PricePerDay = price,
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
            api.MapGet("/my-rentals", [Authorize] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                string? status,
                DateTime? from,
                DateTime? to) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var tid = tenantProvider.GetCurrentTenantId();

                var query = db.Rentals
                    .AsNoTracking()
                    .Include(r => r.Items)
                        .ThenInclude(i => i.Product)
                    .Where(r => tid == null || r.TenantId == tid);

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
                    .Select(r => new MyRentalDto
                    {
                        Id = r.Id,
                        Title = r.Items.Count == 0
                            ? "Wynajem"
                            : (r.Items.Select(i => i.Product!.Name).FirstOrDefault() ?? "Wynajem") + (r.Items.Count > 1 ? $" (+{r.Items.Count - 1})" : string.Empty),
                        StartDateUtc = r.StartDateUtc,
                        EndDateUtc = r.EndDateUtc,
                        Quantity = r.Items.Sum(i => i.Quantity),
                        TotalAmount = r.TotalAmount,
                        Status = r.Status.ToString(),
                        CanCancel = r.Status != RentalStatus.Cancelled && r.StartDateUtc > DateTime.UtcNow
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
                var product = await db.Products.FirstOrDefaultAsync(p => p.Id == req.ProductId);
                if (product == null) return Results.NotFound("Nie znaleziono produktu");

                // Tenant bierzemy Z PRODUKTU, nie z headera!
                var tid = product.TenantId;

                var ttl = Math.Clamp(req.TtlMinutes ?? 10, 5, 30);
                var nowUtc = DateTime.UtcNow;

                // policz zajętość: aktywne rezerwacje + aktywne holdy
                var overlappingReservedQty = await db.RentalItems
                    .Where(ri => ri.ProductId == req.ProductId)
                    .Join(db.Rentals, ri => ri.RentalId, r => r.Id, (ri, r) => new { ri, r })
                    .Where(x => x.r.TenantId == tid
                                && x.r.Status != RentalStatus.Cancelled
                                && x.r.EndDateUtc > req.StartDateUtc
                                && x.r.StartDateUtc < req.EndDateUtc)
                    .SumAsync(x => (int?)x.ri.Quantity) ?? 0;

                var activeHoldsQty = await db.ReservationHolds
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
                var tid = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                var hold = await db.ReservationHolds.FirstOrDefaultAsync(h => h.Id == id && (tid == Guid.Empty || h.TenantId == tid));
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
    }

    // DTOs for auth endpoints
    public record RegisterRequest(string Email, string Password, string? FullName, string? PhoneNumber, string? DocumentNumber);
    public record LoginRequest(string Email, string Password);
}

