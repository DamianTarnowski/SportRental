using System.Security.Claims;
using SportRental.Admin.Api.Models;
using SportRental.Admin.Services;
using SportRental.Admin.Services.Auth;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using SportRental.Admin.Services.Contracts;
using SportRental.Admin.Services.Sms;
using SportRental.Admin.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using System.Data;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.WebUtilities;
using SportRental.Shared.Legal;
using SportRental.Shared.Identity;
using SportRental.Shared.Time;
using SharedModels = SportRental.Shared.Models;

namespace SportRental.Admin.Api
{
    public static class Endpoints
    {
        // Accepts both JWT Bearer (WASM) and Identity cookie (Blazor Server).
        private const string ApiAuthSchemes = JwtBearerDefaults.AuthenticationScheme + ",Identity.Application";

        /// <summary>
        /// Wyciąga (TenantId, IsSuperAdmin) z claims. Dla SuperAdmin TenantId może być
        /// pustym (Guid.Empty) — zwracamy IsSuperAdmin=true i caller ma prawo do cross-tenant.
        /// Dla zwykłego usera bez tenant claim zwracamy (Guid.Empty, false) — endpoint powinien
        /// zwrócić Forbid.
        /// </summary>
        private static (Guid TenantId, bool IsSuperAdmin) ResolveTenantContext(ClaimsPrincipal user)
        {
            var isSuper = user.IsInRole("SuperAdmin");
            var tid = user.FindFirst("tenant-id")?.Value;
            return (Guid.TryParse(tid, out var t) ? t : Guid.Empty, isSuper);
        }

        private static string GenerateSessionId()
        {
            Span<byte> buffer = stackalloc byte[32];
            RandomNumberGenerator.Fill(buffer);
            return Convert.ToBase64String(buffer).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private static bool SessionIdEquals(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            var ab = System.Text.Encoding.UTF8.GetBytes(a);
            var bb = System.Text.Encoding.UTF8.GetBytes(b);
            if (ab.Length != bb.Length) return false;
            return CryptographicOperations.FixedTimeEquals(ab, bb);
        }

        private static string ResolveClientReturnUrl(string? returnUrl)
            => SafeReturnUrl.ResolveClient(returnUrl);

        private static bool CanUseClientApplication(IEnumerable<string> roles) =>
            roles.Any(role =>
                string.Equals(role, RoleNames.Client, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, RoleNames.Owner, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, RoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase));

        private static Guid? ResolvePublicCatalogTenantId(
            HttpRequest request,
            Guid? explicitTenantId = null)
        {
            // Query string jest kanonicznym filtrem aktualnego WASM. Nagłówek zostaje
            // wyłącznie jako kompatybilny fallback dla starszych klientów ApiService.
            // Nie korzystamy tu z ITenantProvider, bo claim staffu jest zakresem
            // administracyjnym, a nie filtrem publicznego marketplace'u.
            if (explicitTenantId.HasValue)
                return explicitTenantId;

            return request.Headers.TryGetValue("X-Tenant-Id", out var headerValue) &&
                   Guid.TryParse(headerValue.ToString(), out var headerTenantId) &&
                   headerTenantId != Guid.Empty
                ? headerTenantId
                : null;
        }

        // SEC-009: nazwa HttpOnly cookie z tokenem dostępu dla WASM.
        // SEC-020: prefix __Host- gwarantuje że cookie jest tylko z Secure + Path=/ + bez Domain
        // (browser odrzuci cookie z tym prefixem jeśli serwer próbuje obejść te wymagania).
        public const string AccessTokenCookieName = "__Host-sr_access_token";

        // SEC-009: zapisuje JWT w HttpOnly cookie (zamiast oddawać go klientowi do localStorage).
        //
        // Client produkcyjny jest same-site pod /_client. Lokalne porty localhost również
        // są same-site, więc Lax ogranicza CSRF bez psucia standalone WASM w dev.
        private static void WriteAccessTokenCookie(HttpContext httpContext, string token, DateTime expiresAtUtc)
        {
            httpContext.Response.Cookies.Append(AccessTokenCookieName, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = expiresAtUtc,
                Path = "/"
            });
        }

        internal static void DeleteAccessTokenCookie(HttpContext httpContext)
        {
            httpContext.Response.Cookies.Delete(AccessTokenCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
        }

        private static async Task SendEmailConfirmationLinkBestEffortAsync(
            ApplicationUser user,
            string email,
            UserManager<ApplicationUser> userManager,
            IEmailSender<ApplicationUser> emailSender,
            IConfiguration configuration,
            ILogger logger)
        {
            try
            {
                var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var configuredBaseUrl = configuration["App:BaseUrl"]?.TrimEnd('/');
                var azureHost = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                var applicationBaseUrl = !string.IsNullOrWhiteSpace(configuredBaseUrl)
                    ? configuredBaseUrl
                    : !string.IsNullOrWhiteSpace(azureHost)
                        ? $"https://{azureHost}"
                        : "http://localhost:5001";
                var confirmationUrl = QueryHelpers.AddQueryString(
                    $"{applicationBaseUrl}/Account/ConfirmEmail",
                    new Dictionary<string, string?>
                    {
                        ["userId"] = user.Id.ToString(),
                        ["code"] = encodedCode,
                        ["returnUrl"] = "/_client/login?emailConfirmed=true"
                    });

                await emailSender.SendConfirmationLinkAsync(
                    user,
                    email,
                    HtmlEncoder.Default.Encode(confirmationUrl));
            }
            catch (Exception ex)
            {
                // Konto już istnieje i użytkownik może użyć standardowego resend flow.
                // Nie logujemy tokenu ani adresu email.
                logger.LogError(ex, "Nie udało się wysłać linku potwierdzającego dla użytkownika {UserId}.", user.Id);
            }
        }

        private static string FormatPublicBusinessHours(
            BusinessHoursSchedule? schedule,
            IReadOnlyCollection<BusinessHoursException> upcomingExceptions)
        {
            static int DayOrder(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;
            static string DayLabel(DayOfWeek day) => day switch
            {
                DayOfWeek.Monday => "Pn",
                DayOfWeek.Tuesday => "Wt",
                DayOfWeek.Wednesday => "Śr",
                DayOfWeek.Thursday => "Cz",
                DayOfWeek.Friday => "Pt",
                DayOfWeek.Saturday => "Sb",
                DayOfWeek.Sunday => "Nd",
                _ => day.ToString()
            };

            string regular;
            if (schedule is null || schedule.Days.Count == 0)
            {
                regular = "Codziennie: 08:00–20:00";
            }
            else
            {
                var days = Enum.GetValues<DayOfWeek>()
                    .OrderBy(DayOrder)
                    .Select(dayOfWeek =>
                    {
                        var day = schedule.Days.FirstOrDefault(value => value.DayOfWeek == dayOfWeek);
                        if (day is null || day.IsClosed || !day.OpenFrom.HasValue || !day.OpenTo.HasValue)
                            return $"{DayLabel(dayOfWeek)}: zamknięte";

                        return $"{DayLabel(dayOfWeek)}: {day.OpenFrom.Value:HH\\:mm}–{day.OpenTo.Value:HH\\:mm}";
                    });
                regular = string.Join("; ", days);
            }

            if (upcomingExceptions.Count == 0)
                return regular;

            var exceptions = upcomingExceptions
                .OrderBy(exception => exception.Date)
                .Take(8)
                .Select(exception =>
                {
                    if (exception.IsClosed)
                        return $"{exception.Date:dd.MM}: zamknięte";
                    if (exception.CustomOpen.HasValue && exception.CustomClose.HasValue)
                        return $"{exception.Date:dd.MM}: {exception.CustomOpen.Value:HH\\:mm}–{exception.CustomClose.Value:HH\\:mm}";
                    return $"{exception.Date:dd.MM}: otwarte cały dzień";
                });

            return $"{regular}. Wyjątki: {string.Join("; ", exceptions)}";
        }

        // Helper to convert relative URLs to absolute URLs
        private static string? ToAbsoluteUrl(string? relativeUrl, HttpRequest request)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl))
                return relativeUrl;

            // Starsze rekordy mogą zawierać mały, wbudowany podgląd zamiast ścieżki
            // do blobu. Nie wolno poprzedzać data URI hostem (powstawał URL 14 kB i 414).
            // Dopuszczamy wyłącznie raster base64; SVG i pozostałe schematy data są
            // odrzucane, bo nie są potrzebne dla zdjęć produktów.
            if (relativeUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                const int maxInlineImageUrlLength = 2 * 1024 * 1024;
                var isAllowedRaster =
                    relativeUrl.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase) ||
                    relativeUrl.StartsWith("data:image/jpg;base64,", StringComparison.OrdinalIgnoreCase) ||
                    relativeUrl.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase) ||
                    relativeUrl.StartsWith("data:image/webp;base64,", StringComparison.OrdinalIgnoreCase) ||
                    relativeUrl.StartsWith("data:image/gif;base64,", StringComparison.OrdinalIgnoreCase);

                return isAllowedRaster && relativeUrl.Length <= maxInlineImageUrlLength
                    ? relativeUrl
                    : null;
            }
            
            // Already absolute URL
            if (relativeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                relativeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return relativeUrl;
            
            // Build absolute URL from request
            var baseUrl = $"{request.Scheme}://{request.Host}";
            return relativeUrl.StartsWith("/") 
                ? $"{baseUrl}{relativeUrl}" 
                : $"{baseUrl}/{relativeUrl}";
        }

        public static IEndpointRouteBuilder MapSportRentalApi(this IEndpointRouteBuilder app)
        {
            // Anonimowy link do umowy chroniony integralnym, nieprzewidywalnym tokenem.
            app.MapGet("/c/{token}", [AllowAnonymous] async (
                string token,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                IFileStorage storage,
                IContractAccessLinkService contractLinks) =>
            {
                if (!contractLinks.TryResolveRentalId(token, out var rentalId))
                    return Results.NotFound("Umowa nie została znaleziona.");

                await using var db = await dbFactory.CreateDbContextAsync();
                var rental = await db.Rentals
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == rentalId);

                if (rental == null || string.IsNullOrWhiteSpace(rental.ContractUrl))
                    return Results.NotFound("Umowa nie została znaleziona.");

                var sasUrl = await storage.GetPrivateReadUrlAsync(
                    rental.ContractUrl, TimeSpan.FromMinutes(10));
                return Results.Redirect(sasUrl);
            }).RequireRateLimiting("api");
            
            var api = app.MapGroup("/api")
                .RequireCors() // Enable CORS for all API endpoints
                .RequireRateLimiting("api"); // SEC-006: 100 req/min/IP baseline
            
            // Auth endpoints
            MapAuthEndpoints(api);

            // Publiczne dane operatora i wersje dokumentów prawnych.
            MapLegalEndpoints(api);
            
            // Customer endpoints for WASM client
            MapCustomerEndpoints(api);

            // Rental review endpoints (public read, auth'd write by rental owner)
            MapReviewEndpoints(api);

            // Customer trust endpoints (admin-only — wystawia ocenę klienta po zwrocie)
            MapCustomerTrustEndpoints(api);

            api.MapGet("/products", [AllowAnonymous] async (
                HttpRequest request,
                IDbContextFactory<ApplicationDbContext> dbFactory, 
                int? page, 
                int? pageSize,
                string? search,
                string? category,
                string? city,
                string? voivodeship,
                string? tenant,
                Guid? tenantId,
                decimal? minPrice,
                decimal? maxPrice,
                bool? available,
                string? sort,
                double? userLat,
                double? userLon) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var catalogTenantId = ResolvePublicCatalogTenantId(request, tenantId);
                
                // Publiczny katalog jest marketplace'em i nie może dziedziczyć tenant scope
                // z JWT Ownera/SuperAdmina. Wypożyczalnię zawężamy wyłącznie jawnym
                // filtrem, identycznie dla gościa i zalogowanego klienta.
                var baseQuery = db.Products.IgnoreQueryFilters().AsNoTracking()
                    .Where(p => p.IsActive && !p.Disabled && !p.IsDeleted);
                
                // Join with Tenants and CompanyInfos
                var query = baseQuery
                    .Join(db.Tenants, p => p.TenantId, t => t.Id, (p, t) => new { Product = p, Tenant = t })
                    .Where(x => !x.Tenant.IsDemo)
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
                        ImageBasePath = x.Product.ImageBasePath,
                        ImageVariantWidths = x.Product.ImageVariantWidths,
                        HasOriginalImage = x.Product.HasOriginalImage,
                        PricePerDay = x.Product.DailyPrice,
                        DailyPrice = x.Product.DailyPrice,
                        HourlyPrice = x.Product.HourlyPrice,
                        Quantity = x.Product.AvailableQuantity,
                        AvailableQuantity = x.Product.AvailableQuantity,
                        IsAvailable = x.Product.Available && x.Product.IsActive && x.Product.AvailableQuantity > 0,
                        PickupAddress = x.CompanyInfo != null ? x.CompanyInfo.Address : null,
                        City = x.Product.City ?? x.CompanyInfo!.City,
                        Voivodeship = x.Product.Voivodeship ?? x.CompanyInfo!.Voivodeship,
                        Lat = x.CompanyInfo != null ? x.CompanyInfo.Lat : (double?)null,
                        Lon = x.CompanyInfo != null ? x.CompanyInfo.Lon : (double?)null
                    });

                // Apply filters
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchCandidates = LegacyPublicTextNormalizer.GetFilterCandidates(search);
                    query = query.Where(p =>
                        p.Name.ToLower().Contains(searchCandidates.Canonical) ||
                        p.Name.ToLower().Contains(searchCandidates.Legacy) ||
                        (p.Description != null &&
                         (p.Description.ToLower().Contains(searchCandidates.Canonical) ||
                          p.Description.ToLower().Contains(searchCandidates.Legacy))));
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    var categoryCandidates = LegacyPublicTextNormalizer.GetFilterCandidates(category);
                    query = query.Where(p =>
                        p.Category != null &&
                        (p.Category.ToLower() == categoryCandidates.Canonical ||
                         p.Category.ToLower() == categoryCandidates.Legacy));
                }

                if (!string.IsNullOrWhiteSpace(city))
                {
                    var cityCandidates = LegacyPublicTextNormalizer.GetFilterCandidates(city);
                    query = query.Where(p =>
                        p.City != null &&
                        (p.City.ToLower() == cityCandidates.Canonical ||
                         p.City.ToLower() == cityCandidates.Legacy));
                }

                if (!string.IsNullOrWhiteSpace(voivodeship))
                {
                    var voivodeshipCandidates = LegacyPublicTextNormalizer.GetFilterCandidates(voivodeship);
                    query = query.Where(p =>
                        p.Voivodeship != null &&
                        (p.Voivodeship.ToLower() == voivodeshipCandidates.Canonical ||
                         p.Voivodeship.ToLower() == voivodeshipCandidates.Legacy));
                }

                if (catalogTenantId.HasValue)
                {
                    query = query.Where(p => p.TenantId == catalogTenantId.Value);
                }
                else if (!string.IsNullOrWhiteSpace(tenant))
                {
                    var tenantCandidates = LegacyPublicTextNormalizer.GetFilterCandidates(tenant);
                    query = query.Where(p =>
                        p.TenantName.ToLower() == tenantCandidates.Canonical ||
                        p.TenantName.ToLower() == tenantCandidates.Legacy);
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
                var availableCount = await query.CountAsync(product => product.IsAvailable);
                var averagePrice = totalCount > 0
                    ? await query.AverageAsync(product => product.DailyPrice)
                    : 0m;
                var minimumPrice = totalCount > 0
                    ? await query.MinAsync(product => product.DailyPrice)
                    : 0m;

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

                // Convert relative ImageUrls to absolute URLs
                var itemsWithAbsoluteUrls = items.Select(p => new
                {
                    p.Id,
                    p.TenantId,
                    TenantName = LegacyPublicTextNormalizer.Normalize((string?)p.TenantName),
                    Name = LegacyPublicTextNormalizer.Normalize((string?)p.Name),
                    p.Sku,
                    Category = LegacyPublicTextNormalizer.Normalize((string?)p.Category),
                    Description = LegacyPublicTextNormalizer.Normalize((string?)p.Description),
                    ImageUrl = ToAbsoluteUrl(p.ImageUrl, request),
                    p.ImageBasePath, p.ImageVariantWidths, p.HasOriginalImage,
                    p.PricePerDay, p.DailyPrice, p.HourlyPrice, p.Quantity, p.AvailableQuantity,
                    p.IsAvailable,
                    PickupAddress = LegacyPublicTextNormalizer.Normalize((string?)p.PickupAddress),
                    City = LegacyPublicTextNormalizer.Normalize((string?)p.City),
                    Voivodeship = LegacyPublicTextNormalizer.Normalize((string?)p.Voivodeship),
                    p.Lat, p.Lon
                }).ToList();

                return Results.Ok(new
                {
                    Items = itemsWithAbsoluteUrls,
                    TotalCount = totalCount,
                    Page = pageNum,
                    PageSize = pageSizeNum,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSizeNum),
                    AvailableCount = availableCount,
                    AveragePrice = Math.Round(averagePrice, 2),
                    MinimumPrice = minimumPrice
                });
            });

            // GET /api/products/facets - lekkie opcje filtrów i statystyki marketplace.
            // Klient nie musi pobierać wszystkich stron produktów przed pokazaniem katalogu.
            api.MapGet("/products/facets", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var rows = await db.Products
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(product => product.IsActive && !product.Disabled && !product.IsDeleted)
                    .Join(
                        db.Tenants,
                        product => product.TenantId,
                        tenant => tenant.Id,
                        (product, tenant) => new { Product = product, Tenant = tenant })
                    .Where(row => !row.Tenant.IsDemo)
                    .GroupJoin(
                        db.CompanyInfos,
                        row => row.Product.TenantId,
                        company => company.TenantId,
                        (row, companies) => new
                        {
                            row.Product,
                            TenantId = row.Tenant.Id,
                            TenantName = row.Tenant.Name,
                            Company = companies.FirstOrDefault()
                        })
                    .Select(row => new
                    {
                        row.TenantId,
                        row.TenantName,
                        row.Product.Category,
                        row.Product.DailyPrice,
                        IsAvailable = row.Product.Available && row.Product.AvailableQuantity > 0,
                        City = row.Product.City ?? row.Company!.City,
                        Voivodeship = row.Product.Voivodeship ?? row.Company!.Voivodeship
                    })
                    .ToListAsync(ct);

                var prices = rows.Select(row => row.DailyPrice).ToList();
                return Results.Ok(new SharedModels.ProductCatalogFacetsDto
                {
                    Categories = rows
                        .Select(row => LegacyPublicTextNormalizer.Normalize(row.Category))
                        .Where(category => !string.IsNullOrWhiteSpace(category))
                        .Select(category => category!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
                        .ToList(),
                    Locations = rows
                        .Select(row => new SharedModels.ProductCatalogLocationFacetDto
                        {
                            City = LegacyPublicTextNormalizer.Normalize(row.City),
                            Voivodeship = LegacyPublicTextNormalizer.Normalize(row.Voivodeship)
                        })
                        .Where(location =>
                            !string.IsNullOrWhiteSpace(location.City) ||
                            !string.IsNullOrWhiteSpace(location.Voivodeship))
                        .DistinctBy(location => new
                        {
                            City = location.City?.ToUpperInvariant(),
                            Voivodeship = location.Voivodeship?.ToUpperInvariant()
                        })
                        .ToList(),
                    Tenants = rows
                        .GroupBy(row => row.TenantId)
                        .Select(group => new SharedModels.ProductCatalogTenantFacetDto
                        {
                            TenantId = group.Key,
                            Name = group.First().TenantName
                        })
                        .OrderBy(tenant => tenant.Name)
                        .ToList(),
                    TotalCount = rows.Count,
                    AvailableCount = rows.Count(row => row.IsAvailable),
                    MinimumPrice = prices.Count > 0 ? prices.Min() : 0m,
                    MaximumPrice = prices.Count > 0 ? prices.Max() : 0m,
                    AveragePrice = prices.Count > 0 ? Math.Round(prices.Average(), 2) : 0m
                });
            });

            // GET /api/products/{id} - pojedynczy produkt
            api.MapGet("/products/{id:guid}", [AllowAnonymous] async (
                HttpRequest request,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                Guid id,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var catalogTenantId = ResolvePublicCatalogTenantId(request);

                var baseQuery = db.Products.IgnoreQueryFilters().AsNoTracking()
                    .Where(p => p.IsActive && !p.Disabled && !p.IsDeleted);
                if (catalogTenantId.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.TenantId == catalogTenantId.Value);
                }

                var product = await baseQuery
                    .Where(p => p.Id == id)
                    .Join(db.Tenants, p => p.TenantId, t => t.Id, (p, t) => new { Product = p, TenantName = t.Name })
                    .Where(x => !db.Tenants.Any(t => t.Id == x.Product.TenantId && t.IsDemo))
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
                        ImageBasePath = x.Product.ImageBasePath,
                        ImageVariantWidths = x.Product.ImageVariantWidths,
                        HasOriginalImage = x.Product.HasOriginalImage,
                        PricePerDay = x.Product.DailyPrice,
                        DailyPrice = x.Product.DailyPrice,
                        HourlyPrice = x.Product.HourlyPrice,
                        Quantity = x.Product.AvailableQuantity,
                        AvailableQuantity = x.Product.AvailableQuantity,
                        IsAvailable = x.Product.Available && x.Product.IsActive && x.Product.AvailableQuantity > 0,
                        PickupAddress = db.CompanyInfos.IgnoreQueryFilters()
                            .Where(info => info.TenantId == x.Product.TenantId)
                            .Select(info => info.Address)
                            .FirstOrDefault(),
                        City = x.Product.City ?? db.CompanyInfos.IgnoreQueryFilters()
                            .Where(info => info.TenantId == x.Product.TenantId)
                            .Select(info => info.City)
                            .FirstOrDefault(),
                        Voivodeship = x.Product.Voivodeship ?? db.CompanyInfos.IgnoreQueryFilters()
                            .Where(info => info.TenantId == x.Product.TenantId)
                            .Select(info => info.Voivodeship)
                            .FirstOrDefault()
                    })
                    .FirstOrDefaultAsync(ct);

                if (product is null) return Results.NotFound();
                
                // Convert relative ImageUrl to absolute URL
                return Results.Ok(new
                {
                    product.Id,
                    product.TenantId,
                    TenantName = LegacyPublicTextNormalizer.Normalize(product.TenantName),
                    Name = LegacyPublicTextNormalizer.Normalize(product.Name),
                    product.Sku,
                    Category = LegacyPublicTextNormalizer.Normalize(product.Category),
                    Description = LegacyPublicTextNormalizer.Normalize(product.Description),
                    ImageUrl = ToAbsoluteUrl(product.ImageUrl, request),
                    product.ImageBasePath, product.ImageVariantWidths, product.HasOriginalImage,
                    product.PricePerDay, product.DailyPrice, product.HourlyPrice,
                    product.Quantity, product.AvailableQuantity, product.IsAvailable,
                    PickupAddress = LegacyPublicTextNormalizer.Normalize(product.PickupAddress),
                    City = LegacyPublicTextNormalizer.Normalize(product.City),
                    Voivodeship = LegacyPublicTextNormalizer.Normalize(product.Voivodeship)
                });
            });

            // GET /api/tenants - lista wszystkich wypożyczalni z produktami
            api.MapGet("/tenants", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                db.SetTenant(null); // Globalny dostęp

                // Pobierz tenant IDs które mają aktywne produkty
                var tenantIdsWithProducts = await db.Products
                    .IgnoreQueryFilters()
                    .Where(p => p.IsActive && !p.Disabled && !p.IsDeleted)
                    .Select(p => p.TenantId)
                    .Distinct()
                    .ToListAsync(ct);

                var tenants = await db.Tenants
                    .AsNoTracking()
                    .Where(t => tenantIdsWithProducts.Contains(t.Id) && !t.IsDemo)
                    .Select(t => new
                    {
                        Id = t.Id,
                        Name = t.Name,
                        LogoUrl = t.LogoUrl
                    })
                    .ToListAsync(ct);

                // Pobierz company info osobno
                var companyInfos = await db.CompanyInfos
                    .AsNoTracking()
                    .Where(ci => tenantIdsWithProducts.Contains(ci.TenantId))
                    .ToDictionaryAsync(ci => ci.TenantId, ct);

                // Zlicz produkty dla każdego tenanta
                var productCounts = await db.Products
                    .IgnoreQueryFilters()
                    .Where(p => p.IsActive && !p.Disabled && !p.IsDeleted && tenantIdsWithProducts.Contains(p.TenantId))
                    .GroupBy(p => p.TenantId)
                    .Select(g => new { TenantId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

                var result = tenants.Select(t => new
                {
                    t.Id,
                    Name = companyInfos.TryGetValue(t.Id, out var ci) && !string.IsNullOrEmpty(ci.Name) ? ci.Name : t.Name,
                    t.LogoUrl,
                    ProductCount = productCounts.GetValueOrDefault(t.Id, 0),
                    City = companyInfos.TryGetValue(t.Id, out var ci2) ? ci2.City : null
                }).ToList();

                return Results.Ok(result);
            });

            // GET /api/tenants/locations - lokalizacje wypożyczalni (dla mapy)
            api.MapGet("/tenants/locations", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var productCounts = await db.Products
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(product => product.IsActive && !product.Disabled && !product.IsDeleted)
                    .GroupBy(product => product.TenantId)
                    .Select(group => new { TenantId = group.Key, Count = group.Count() })
                    .ToDictionaryAsync(row => row.TenantId, row => row.Count, ct);

                var locationRows = await db.CompanyInfos
                    .AsNoTracking()
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
                        LogoUrl = t.LogoUrl
                    })
                    .Where(x => !db.Tenants.Any(t => t.Id == x.TenantId && t.IsDemo))
                    .ToListAsync(ct);

                var tenantIds = locationRows.Select(location => location.TenantId).Distinct().ToList();
                var schedules = await db.BusinessHoursSchedules
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(schedule => schedule.Days)
                    .Where(schedule => tenantIds.Contains(schedule.TenantId))
                    .ToListAsync(ct);
                var scheduleByTenant = schedules.ToDictionary(schedule => schedule.TenantId);

                var today = DateOnly.FromDateTime(PolishRentalTime.TodayLocal);
                var exceptionHorizon = today.AddDays(90);
                var exceptions = await db.BusinessHoursExceptions
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(exception => tenantIds.Contains(exception.TenantId) &&
                                        exception.Date >= today &&
                                        exception.Date <= exceptionHorizon)
                    .ToListAsync(ct);
                var exceptionsByTenant = exceptions
                    .GroupBy(exception => exception.TenantId)
                    .ToDictionary(group => group.Key, group => (IReadOnlyCollection<BusinessHoursException>)group.ToList());

                var locations = locationRows.Select(location => new SharedModels.TenantLocationDto
                {
                    TenantId = location.TenantId,
                    TenantName = location.TenantName,
                    Lat = location.Lat,
                    Lon = location.Lon,
                    Address = LegacyPublicTextNormalizer.Normalize(location.Address),
                    City = LegacyPublicTextNormalizer.Normalize(location.City),
                    Voivodeship = LegacyPublicTextNormalizer.Normalize(location.Voivodeship),
                    PhoneNumber = location.PhoneNumber,
                    Email = location.Email,
                    OpeningHours = FormatPublicBusinessHours(
                        scheduleByTenant.GetValueOrDefault(location.TenantId),
                        exceptionsByTenant.GetValueOrDefault(location.TenantId) ?? []),
                    LogoUrl = location.LogoUrl,
                    ProductCount = productCounts.GetValueOrDefault(location.TenantId)
                }).ToList();

                return Results.Ok(locations);
            });

            // POST /api/contact - publiczny formularz kontaktowy klienta do wybranej wypożyczalni.
            api.MapPost("/contact", [AllowAnonymous] async (
                SharedModels.ContactMessageRequest req,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                SportRental.Admin.Services.Email.IEmailSender emailSender,
                IConfiguration configuration,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var validationResults = new List<ValidationResult>();
                if (!Validator.TryValidateObject(
                        req,
                        new ValidationContext(req),
                        validationResults,
                        validateAllProperties: true))
                {
                    var errors = validationResults
                        .SelectMany(result =>
                        {
                            var members = result.MemberNames.Any()
                                ? result.MemberNames
                                : [string.Empty];
                            return members.Select(member => new
                            {
                                Member = member,
                                Message = result.ErrorMessage ?? "Nieprawidłowa wartość."
                            });
                        })
                        .GroupBy(error => error.Member)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Select(error => error.Message).Distinct().ToArray());

                    return Results.ValidationProblem(errors);
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var target = await db.Tenants
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(tenant => tenant.Id == req.TenantId)
                    .Select(tenant => new
                    {
                        tenant.Id,
                        tenant.Name,
                        tenant.IsDemo,
                        Email = db.CompanyInfos
                            .IgnoreQueryFilters()
                            .Where(info => info.TenantId == tenant.Id)
                            .Select(info => info.Email)
                            .FirstOrDefault(),
                        AdminEmail = db.CompanyInfos
                            .IgnoreQueryFilters()
                            .Where(info => info.TenantId == tenant.Id)
                            .Select(info => info.AdminEmail)
                            .FirstOrDefault()
                    })
                    .FirstOrDefaultAsync(ct);

                var recipientEmail = !string.IsNullOrWhiteSpace(target?.Email)
                    ? target.Email
                    : target?.AdminEmail;

                // Nie ujawniamy, czy podany identyfikator istnieje ani jaki adres jest skonfigurowany.
                if (target is null || (!target.IsDemo && string.IsNullOrWhiteSpace(recipientEmail)))
                {
                    return Results.UnprocessableEntity(new
                    {
                        error = "Wybrana wypożyczalnia nie obsługuje obecnie wiadomości online."
                    });
                }

                var logger = loggerFactory.CreateLogger("SportRental.Admin.Api.Contact");
                if (target.IsDemo)
                {
                    logger.LogInformation(
                        "DEMO SANDBOX: suppressed public contact message for tenant {TenantId}",
                        target.Id);
                    return Results.Accepted();
                }

                if (!(configuration.GetValue<bool?>("Email:Smtp:Enabled") ?? false))
                {
                    return Results.Json(
                        new { error = "Wysyłka wiadomości jest chwilowo niedostępna. Skontaktuj się z wypożyczalnią bezpośrednio." },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                static string Encode(string value) =>
                    System.Net.WebUtility.HtmlEncode(value.Trim());

                static string EncodeMultiline(string value) =>
                    Encode(value)
                        .Replace("\r\n", "\n", StringComparison.Ordinal)
                        .Replace('\r', '\n')
                        .Replace("\n", "<br />", StringComparison.Ordinal);

                var safeSubject = req.Subject
                    .Replace('\r', ' ')
                    .Replace('\n', ' ')
                    .Trim();
                var phoneRow = string.IsNullOrWhiteSpace(req.Phone)
                    ? string.Empty
                    : $"<p><strong>Telefon:</strong> {Encode(req.Phone)}</p>";
                var html = $"""
                    <h2>Nowa wiadomość z formularza RentSpot</h2>
                    <p><strong>Wypożyczalnia:</strong> {Encode(target.Name)}</p>
                    <p><strong>Nadawca:</strong> {Encode(req.Name)}</p>
                    <p><strong>Email:</strong> {Encode(req.Email)}</p>
                    {phoneRow}
                    <p><strong>Temat:</strong> {Encode(safeSubject)}</p>
                    <p><strong>Wiadomość:</strong><br />{EncodeMultiline(req.Message)}</p>
                    """;

                await emailSender.SendEmailAsync(
                    recipientEmail!,
                    $"[RentSpot] {safeSubject}",
                    html);

                return Results.Accepted();
            }).RequireRateLimiting("auth");

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
                    var tenantIds = computation.Tenants.Select(group => group.TenantId).Distinct().ToList();
                    var tenantNames = await db.Tenants.IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(tenant => tenantIds.Contains(tenant.Id))
                        .ToDictionaryAsync(tenant => tenant.Id, tenant => tenant.Name, ct);
                    var companyInfos = await db.CompanyInfos.IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(info => tenantIds.Contains(info.TenantId))
                        .ToDictionaryAsync(info => info.TenantId, ct);

                    return Results.Ok(new SharedModels.PaymentQuoteResponse
                    {
                        TotalAmount = computation.TotalAmount,
                        DepositAmount = computation.DepositAmount,
                        Currency = "PLN",
                        RentalDays = computation.RentalDays,
                        RentalCount = computation.Tenants.Count,
                        Items = computation.Tenants
                            .SelectMany(t => t.Items)
                            .Select(item => new SharedModels.PaymentQuoteItemBreakdown
                            {
                                ProductId = item.ProductId,
                                Subtotal = item.Subtotal
                            })
                            .ToList(),
                        Tenants = computation.Tenants
                            .Select(group =>
                            {
                                companyInfos.TryGetValue(group.TenantId, out var company);
                                var terms = BuildRentalTermsSummary(company);
                                return new SharedModels.TenantQuoteBreakdown
                                {
                                    TenantId = group.TenantId,
                                    TenantName = tenantNames.GetValueOrDefault(group.TenantId) ?? "Wypożyczalnia",
                                    PickupAddress = company?.Address,
                                    PickupCity = company?.City,
                                    PhoneNumber = company?.PhoneNumber,
                                    Email = company?.Email,
                                    OpeningHours = company?.OpeningHours,
                                    StartDateUtc = group.StartDateUtc,
                                    EndDateUtc = group.EndDateUtc,
                                    RentalType = group.RentalType,
                                    HoursRented = group.HoursRented,
                                    RentalDays = group.RentalDays,
                                    TotalAmount = group.TotalAmount,
                                    DepositAmount = group.DepositAmount,
                                    RentalTerms = terms
                                };
                            })
                            .ToList()
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            // Checkout endpoints for Stripe redirect flow
            MapCheckoutEndpoints(api);
            
            if (app.ServiceProvider.GetRequiredService<IConfiguration>()
                .GetValue<bool>(SmsRoutingSettings.LegacyReplyConfirmationEnabledKey))
            {
#pragma warning disable CS0618 // Deliberately available only through an opt-in legacy flag.
                MapLegacySmsConfirmationEndpoints(api);
#pragma warning restore CS0618
            }

            api.MapPost("/rentals", [Authorize(Roles = "Owner,Employee,SuperAdmin")] async (
                CreateRentalRequest req,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                IContractGenerator contracts,
                IRentalConfirmationService confirmations,
                IFileStorage storage,
                ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("RentalEndpoints");
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
                    var storageReference = await storage.SavePrivateAsync(relativePath, pdf);
                    rental.ContractUrl = storageReference;
                    db.Rentals.Update(rental);
                    await db.SaveChangesAsync();

                    var demoState = await db.Tenants.IgnoreQueryFilters()
                        .Where(t => t.Id == tid)
                        .Select(t => (bool?)t.IsDemo)
                        .FirstOrDefaultAsync();
                    var isDemoTenant = demoState ?? true;

                    if (!isDemoTenant && !string.IsNullOrWhiteSpace(customer.Email))
                    {
                        try
                        {
                            await contracts.SendRentalConfirmationEmailAsync(
                                rental, items, customer, productMap.Values, companyInfo,
                                template?.Content);
                            rental.IsEmailSent = true;
                            await db.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex,
                                "Wysyłka umowy e-mailem nie powiodła się dla wynajmu {RentalId}",
                                rental.Id);
                        }
                    }

                    if (!isDemoTenant
                        && companyInfo?.SmsConfirmationEnabled == true
                        && (!string.IsNullOrWhiteSpace(customer.PhoneNumber)
                            || !string.IsNullOrWhiteSpace(customer.Email)))
                    {
                        try
                        {
                            var token = await confirmations.CreateConfirmationForTenantAsync(tid, rental.Id);
                            var delivery = await confirmations.SendConfirmationLinkForTenantAsync(
                                tid, rental.Id, token);
                            if (!delivery.AnySent)
                            {
                                logger.LogWarning(
                                    "Nie udało się wysłać linku potwierdzenia dla wynajmu {RentalId}",
                                    rental.Id);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex,
                                "Wysyłka linku potwierdzenia nie powiodła się dla wynajmu {RentalId}",
                                rental.Id);
                        }
                    }

                    return Results.Created($"/api/rentals/{rental.Id}", new { rental.Id, rental.TotalAmount, rental.ContractUrl });
                }
            });

            api.MapGet("/contracts/{id:guid}", [Authorize] async (Guid id, IDbContextFactory<ApplicationDbContext> dbFactory, IFileStorage storage, ClaimsPrincipal user) =>
            {
                // SEC: ownership check. Bez tego query filter `TenantId == null || ...`
                // pozwalał komuś z tenanta A pobrać kontrakt B podając jego rental Id.
                var (callerTenant, callerIsSuperAdmin) = ResolveTenantContext(user);
                if (callerTenant == Guid.Empty && !callerIsSuperAdmin) return Results.Forbid();

                await using var db = await dbFactory.CreateDbContextAsync();
                var rental = await db.Rentals.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id);
                if (rental == null || string.IsNullOrWhiteSpace(rental.ContractUrl))
                    return Results.NotFound();
                if (!callerIsSuperAdmin && rental.TenantId != callerTenant)
                    return Results.NotFound(); // 404 zamiast 403 — nie ujawniamy istnienia rekordu cross-tenant

                var sasUrl = await storage.GetPrivateReadUrlAsync(rental.ContractUrl, TimeSpan.FromMinutes(10));
                return Results.Redirect(sasUrl);
            });

            // Faza 9a — walidacja kodu rabatowego (publiczny endpoint do checkout client)
            api.MapPost("/discount-codes/validate", [AllowAnonymous] async (
                SportRental.Shared.Models.DiscountValidateRequest req,
                SportRental.Admin.Services.IDiscountService discounts,
                CancellationToken ct) =>
            {
                if (req == null || string.IsNullOrWhiteSpace(req.Code) || req.TenantId == Guid.Empty)
                    return Results.BadRequest(new { error = "Niepełne dane." });
                var r = await discounts.ValidateAsync(req.TenantId, req.Code, req.OrderAmount, ct);
                return Results.Ok(new { isValid = r.IsValid, discountAmount = r.DiscountAmount, reason = r.Reason });
            }).RequireRateLimiting("api");

            // Faza 9b — walidacja vouchera
            api.MapPost("/vouchers/validate", [AllowAnonymous] async (
                SportRental.Shared.Models.VoucherValidateRequest req,
                SportRental.Admin.Services.IVoucherService vouchers,
                CancellationToken ct) =>
            {
                if (req == null || string.IsNullOrWhiteSpace(req.Code))
                    return Results.BadRequest(new { error = "Pusty kod." });
                var r = await vouchers.ValidateAsync(req.Code, ct);
                return Results.Ok(new { isValid = r.IsValid, remainingBalance = r.RemainingBalance, reason = r.Reason });
            }).RequireRateLimiting("api");

            // Faza 9c — Google Calendar OAuth flow (admin redirect → Google → callback)
            api.MapGet("/google-calendar/connect", [Authorize(Roles = "Owner,SuperAdmin")] (
                HttpRequest request,
                SportRental.Admin.Services.IGoogleCalendarService gcal,
                ITenantProvider tenantProvider) =>
            {
                var tenantId = tenantProvider.GetCurrentTenantId();
                if (tenantId == null) return Results.BadRequest(new { error = "Brak tenanta." });
                var redirectUri = $"{request.Scheme}://{request.Host}/api/google-calendar/callback";
                return Results.Redirect(gcal.BuildAuthorizationUrl(redirectUri, tenantId.Value));
            });

            api.MapGet("/google-calendar/callback", [AllowAnonymous] async (
                HttpRequest request,
                string? code,
                string? state,
                string? error,
                SportRental.Admin.Services.IGoogleCalendarService gcal,
                CancellationToken ct) =>
            {
                if (!string.IsNullOrWhiteSpace(error))
                    return Results.Redirect("/admin/google-calendar?error=" + Uri.EscapeDataString(error));
                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                    return Results.BadRequest(new { error = "Brak code lub state." });
                if (!Guid.TryParseExact(state, "N", out var tenantId))
                    return Results.BadRequest(new { error = "Niepoprawny state." });

                var redirectUri = $"{request.Scheme}://{request.Host}/api/google-calendar/callback";
                try
                {
                    await gcal.ConnectTenantAsync(tenantId, code, redirectUri, ct);
                    return Results.Redirect("/admin/google-calendar?connected=1");
                }
                catch (Exception ex)
                {
                    return Results.Redirect("/admin/google-calendar?error=" + Uri.EscapeDataString(ex.Message));
                }
            });

            // Faza 8c — faktury VAT
            // POST /api/rentals/{id}/invoice — wystawia fakturę dla wynajmu (idempotent przez Number unique)
            api.MapPost("/rentals/{id:guid}/invoice", [Authorize(Roles = "Owner,SuperAdmin")] async (
                Guid id,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                SportRental.Admin.Services.IInvoiceService invoices,
                ClaimsPrincipal user) =>
            {
                var (callerTenant, callerIsSuperAdmin) = ResolveTenantContext(user);
                if (callerTenant == Guid.Empty && !callerIsSuperAdmin) return Results.Forbid();

                await using var db = await dbFactory.CreateDbContextAsync();
                var rental = await db.Rentals.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id);
                if (rental == null) return Results.NotFound();
                if (!callerIsSuperAdmin && rental.TenantId != callerTenant) return Results.NotFound();

                // Idempotency: jeśli już istnieje invoice dla tego rentala, zwróć go (nie generuj ponownie)
                var existing = await db.Invoices.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.RentalId == id);
                if (existing != null)
                    return Results.Ok(new { id = existing.Id, number = existing.Number, status = existing.Status.ToString() });

                var inv = await invoices.CreateForRentalAsync(id);
                return Results.Created($"/api/invoices/{inv.Id}", new { id = inv.Id, number = inv.Number, status = inv.Status.ToString() });
            });

            // GET /api/invoices/{id}/pdf — pobierz PDF
            api.MapGet("/invoices/{id:guid}/pdf", [Authorize] async (
                Guid id,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                SportRental.Admin.Services.IInvoiceService invoices,
                ClaimsPrincipal user) =>
            {
                var (callerTenant, callerIsSuperAdmin) = ResolveTenantContext(user);
                if (callerTenant == Guid.Empty && !callerIsSuperAdmin) return Results.Forbid();

                await using var db = await dbFactory.CreateDbContextAsync();
                var inv = await db.Invoices.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id);
                if (inv == null) return Results.NotFound();
                if (!callerIsSuperAdmin && inv.TenantId != callerTenant) return Results.NotFound();

                var pdf = await invoices.GeneratePdfAsync(id);
                return Results.File(pdf, "application/pdf", fileDownloadName: $"{inv.Number.Replace('/', '_')}.pdf");
            });

            // Upload zdjęcia produktu
            api.MapPost("/products/{id:guid}/image", [Authorize(Roles = "Owner,SuperAdmin")] async (Guid id, HttpRequest request, IDbContextFactory<ApplicationDbContext> dbFactory, ImageVariantService images, IConfiguration config, ClaimsPrincipal user) =>
            {
                // SEC: ownership check — Owner może zmieniać zdjęcia tylko swojego tenanta;
                // SuperAdmin cross-tenant. Bez checka Owner z tenanta A mógł nadpisać zdjęcie produktu B.
                var (callerTenant, callerIsSuperAdmin) = ResolveTenantContext(user);
                if (callerTenant == Guid.Empty && !callerIsSuperAdmin) return Results.Forbid();

                await using var db = await dbFactory.CreateDbContextAsync();
                var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
                if (product == null) return Results.NotFound();
                if (!callerIsSuperAdmin && product.TenantId != callerTenant)
                    return Results.NotFound();
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
                product.ImageVariantWidths = variants.Keys.OrderBy(width => width).ToArray();
                product.HasOriginalImage = true;
                db.Products.Update(product);
                await db.SaveChangesAsync();
                return Results.Ok(new
                {
                    imageUrl = product.ImageUrl,
                    basePath = product.ImageBasePath,
                    variants,
                    imageVariantWidths = product.ImageVariantWidths,
                    hasOriginalImage = product.HasOriginalImage
                });
            });

            // Upload logo tenanta
            api.MapPost("/tenants/{id:guid}/logo", [Authorize(Roles = "Owner,SuperAdmin")] async (Guid id, HttpRequest request, IDbContextFactory<ApplicationDbContext> dbFactory, IFileStorage storage, IConfiguration config, ClaimsPrincipal user) =>
            {
                // SEC: ownership check — Owner może zmieniać logo TYLKO swojego tenanta.
                // Bez tego Owner z tenanta A mógł nadpisać logo tenanta B podając jego id.
                var (callerTenant, callerIsSuperAdmin) = ResolveTenantContext(user);
                if (callerTenant == Guid.Empty && !callerIsSuperAdmin) return Results.Forbid();
                if (!callerIsSuperAdmin && id != callerTenant) return Results.NotFound();

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
            api.MapDelete("/rentals/{id:guid}", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                Guid id,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ClaimsPrincipal user,
                IServiceProvider services,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await using var transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);
                var usesPostgresRowLocks = db.Database.ProviderName?.Contains(
                    "Npgsql",
                    StringComparison.OrdinalIgnoreCase) == true;
                var rental = usesPostgresRowLocks
                    ? await db.Rentals
                        .FromSqlInterpolated($"SELECT * FROM \"Rentals\" WHERE \"Id\" = {id} FOR UPDATE")
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(ct)
                    : await db.Rentals.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(r => r.Id == id, ct);
                if (rental == null)
                    return Results.NotFound();

                MarketplaceOrder? marketplaceOrder = null;
                if (rental.MarketplaceOrderId.HasValue)
                {
                    marketplaceOrder = usesPostgresRowLocks
                        ? await db.MarketplaceOrders
                            .FromSqlInterpolated($"SELECT * FROM \"MarketplaceOrders\" WHERE \"Id\" = {rental.MarketplaceOrderId.Value} FOR UPDATE")
                            .FirstOrDefaultAsync(ct)
                        : await db.MarketplaceOrders
                            .FirstOrDefaultAsync(order => order.Id == rental.MarketplaceOrderId.Value, ct);
                }

                var customerId = user.GetCustomerId();
                var isStaff = user.IsAdmin();
                var isSuperAdmin = user.IsInRole(SportRental.Shared.Identity.RoleNames.SuperAdmin);
                var staffTenantId = user.GetTenantId();

                var ownedByCustomer = customerId.HasValue && rental.CustomerId == customerId.Value;
                var managedByStaff = isStaff && (isSuperAdmin || staffTenantId == rental.TenantId);
                if (!ownedByCustomer && !managedByStaff)
                    return Results.NotFound();

                if (rental.Status == RentalStatus.Cancelled)
                    return Results.Ok(new { id = rental.Id, status = rental.Status.ToString() });

                if (ownedByCustomer &&
                    (rental.Status is not (RentalStatus.Pending or RentalStatus.Confirmed) ||
                     rental.StartDateUtc <= DateTime.UtcNow))
                {
                    return Results.Conflict(new
                    {
                        error = "Wypożyczenia nie można już anulować online.",
                        status = rental.Status.ToString()
                    });
                }

                if (managedByStaff && rental.Status == RentalStatus.Completed)
                    return Results.Conflict(new { error = "Zakończonego wypożyczenia nie można anulować." });

                var refunded = false;
                var paidAmount = SportRental.Admin.Services.Guards.RentalGuards.GetPaidAmount(rental);
                var depositCollected = rental.DepositAmount > 0m &&
                    SportRental.Admin.Services.Guards.RentalGuards.IsDepositCollected(rental);
                if (paidAmount > 0m)
                {
                    return Results.Conflict(new
                    {
                        error = "Opłaconego wynajmu nie można anulować automatycznie. Skontaktuj się z wypożyczalnią w sprawie rozliczenia."
                    });
                }

                if (depositCollected)
                {
                    if (string.IsNullOrWhiteSpace(rental.PaymentIntentId))
                    {
                        return Results.Conflict(new
                        {
                            error = "Wpłaconego wynajmu nie można anulować automatycznie. Skontaktuj się z wypożyczalnią w sprawie zwrotu."
                        });
                    }

                    try
                    {
                        var gateway = services.GetRequiredService<Payments.IPaymentGateway>();
                        refunded = await gateway.RefundPaymentAsync(
                            Guid.Empty,
                            rental.PaymentIntentId,
                            rental.DepositAmount,
                            "requested_by_customer",
                            $"cancel-rental:{rental.Id:N}");
                    }
                    catch (Exception ex)
                    {
                        loggerFactory.CreateLogger("RentalCancellation")
                            .LogError(ex, "Nie udało się zlecić zwrotu Stripe dla wynajmu {RentalId}", rental.Id);
                    }

                    if (!refunded)
                    {
                        return Results.Json(
                            new { error = "Nie udało się zlecić zwrotu płatności. Wynajem nie został anulowany." },
                            statusCode: StatusCodes.Status502BadGateway);
                    }

                    rental.PaymentStatus = "DepositRefunded";
                    rental.PaidAmount = 0m;
                    rental.DepositPaidAtUtc = null;
                }

                rental.Status = RentalStatus.Cancelled;
                // Po skutecznym Stripe refundzie lokalne rozliczenie musi się
                // zakończyć nawet, jeśli klient zamknął połączenie HTTP.
                var persistenceCt = refunded ? CancellationToken.None : ct;

                if (marketplaceOrder is not null)
                {
                    if (refunded)
                        Payments.MarketplaceOrderAccounting.ApplyRefund(
                            marketplaceOrder,
                            rental.DepositAmount,
                            DateTime.UtcNow);

                    var hasUncancelledRental = await db.Rentals.IgnoreQueryFilters()
                        .AnyAsync(other =>
                            other.MarketplaceOrderId == marketplaceOrder.Id &&
                            other.Id != rental.Id &&
                            other.Status != RentalStatus.Cancelled,
                            persistenceCt);
                    Payments.MarketplaceOrderAccounting.MarkRentalCancelled(
                        marketplaceOrder,
                        hasUncancelledRental,
                        DateTime.UtcNow);
                }

                await db.SaveChangesAsync(persistenceCt);
                await transaction.CommitAsync(persistenceCt);
                return Results.Ok(new { id = rental.Id, status = rental.Status.ToString(), refunded });
            });

            // Lista wynajmów zalogowanego użytkownika/klienta (tenant scoped).
            // customer-id pobierane z tokena, nie z query — zapobiega IDOR (SEC-A03).
            api.MapGet("/my-rentals", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                IContractAccessLinkService contractLinks,
                System.Security.Claims.ClaimsPrincipal user,
                string? status,
                DateTime? from,
                DateTime? to) =>
            {
                var customerId = user.GetCustomerId();
                if (customerId is null)
                {
                    return Results.Forbid();
                }

                await using var db = await dbFactory.CreateDbContextAsync();
                var query = db.Rentals
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(r => r.Items)
                        .ThenInclude(i => i.Product)
                    .Include(r => r.Customer)
                    .Include(r => r.MarketplaceOrder)
                    .Where(r => r.CustomerId == customerId.Value);

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
                        TenantId = r.TenantId,
                        MarketplaceOrderId = r.MarketplaceOrderId,
                        MarketplaceOrderNumber = r.MarketplaceOrder != null
                            ? r.MarketplaceOrder.OrderNumber
                            : null,
                        OrderSequence = r.OrderSequence,
                        OrderRentalCount = r.MarketplaceOrderId == null
                            ? 1
                            : db.Rentals.IgnoreQueryFilters().Count(other =>
                                other.MarketplaceOrderId == r.MarketplaceOrderId),
                        TenantName = db.Tenants.IgnoreQueryFilters()
                            .Where(t => t.Id == r.TenantId)
                            .Select(t => t.Name)
                            .FirstOrDefault() ?? string.Empty,
                        PickupAddress = db.CompanyInfos.IgnoreQueryFilters()
                            .Where(ci => ci.TenantId == r.TenantId)
                            .Select(ci => ci.Address)
                            .FirstOrDefault(),
                        PickupCity = db.CompanyInfos.IgnoreQueryFilters()
                            .Where(ci => ci.TenantId == r.TenantId)
                            .Select(ci => ci.City)
                            .FirstOrDefault(),
                        TenantPhoneNumber = db.CompanyInfos.IgnoreQueryFilters()
                            .Where(ci => ci.TenantId == r.TenantId)
                            .Select(ci => ci.PhoneNumber)
                            .FirstOrDefault(),
                        TenantEmail = db.CompanyInfos.IgnoreQueryFilters()
                            .Where(ci => ci.TenantId == r.TenantId)
                            .Select(ci => ci.Email)
                            .FirstOrDefault(),
                        OpeningHours = db.CompanyInfos.IgnoreQueryFilters()
                            .Where(ci => ci.TenantId == r.TenantId)
                            .Select(ci => ci.OpeningHours)
                            .FirstOrDefault(),
                        Title = r.Items.Count == 0
                            ? "Wynajem"
                            : (r.Items.Select(i => i.Product!.Name).FirstOrDefault() ?? "Wynajem") + (r.Items.Count > 1 ? $" (+{r.Items.Count - 1})" : string.Empty),
                        CustomerName = r.Customer != null ? r.Customer.FullName : string.Empty,
                        StartDateUtc = r.StartDateUtc,
                        EndDateUtc = r.EndDateUtc,
                        Quantity = r.Items.Sum(i => i.Quantity),
                        TotalAmount = r.TotalAmount,
                        DepositAmount = r.DepositAmount,
                        PaidAmount = r.PaidAmount,
                        DepositPaidAtUtc = r.DepositPaidAtUtc,
                        PaymentStatus = r.PaymentStatus,
                        Status = r.Status.ToString(),
                        CanCancel = (r.Status == RentalStatus.Pending || r.Status == RentalStatus.Confirmed) &&
                                    r.StartDateUtc > DateTime.UtcNow &&
                                    r.PaidAmount <= 0m &&
                                    r.PaymentStatus != "Paid" &&
                                    r.PaymentStatus != "paid" &&
                                    r.PaymentStatus != "Succeeded" &&
                                    r.PaymentStatus != "succeeded" &&
                                    (r.DepositAmount <= 0m ||
                                     r.ReturnDepositRefund != null ||
                                     r.PaymentStatus == "DepositRefunded" ||
                                     r.PaymentStatus == "depositrefunded" ||
                                     (r.DepositPaidAtUtc == null &&
                                      r.PaymentStatus != "DepositPaid" &&
                                      r.PaymentStatus != "depositpaid") ||
                                     r.PaymentIntentId != null),
                        ContractUrl = r.ContractUrl,
                        HasReview = db.RentalReviews.IgnoreQueryFilters().Any(rv => rv.RentalId == r.Id),
                        RentalType = (SharedModels.RentalTypeDto)(int)r.RentalType,
                        HoursRented = r.HoursRented,
                        PaymentMethod = r.PaymentMethod,
                        PaidAtUtc = r.PaidAtUtc,
                        DamageCharge = r.DamageCharge,
                        IsSmsConfirmed = r.IsSmsConfirmed,
                        // Nowe pola do śledzenia wydania/zwrotu
                        IssuedAtUtc = r.IssuedAtUtc,
                        ReturnedAtUtc = r.ReturnedAtUtc,
                        IssueNotes = r.IssueNotes,
                        ReturnNotes = r.ReturnNotes,
                        ReturnDepositRefund = r.ReturnDepositRefund,
                        Items = r.Items.Select(i => new SharedModels.MyRentalItemDto
                        {
                            RentalItemId = i.Id,
                            ProductId = i.ProductId,
                            ProductName = i.Product != null ? i.Product.Name : string.Empty,
                            Quantity = i.Quantity,
                            DailyPrice = i.PricePerDay,
                            HourlyPrice = i.PricePerHour,
                            TotalPrice = i.Subtotal
                        }).ToList()
                    })
                    .ToListAsync();

                foreach (var rental in list)
                {
                    rental.PublicContractUrl = string.IsNullOrWhiteSpace(rental.ContractUrl)
                        ? null
                        : contractLinks.CreatePath(rental.Id);
                }

                return Results.Ok(list);
            });

            // Utworzenie krótkotrwałego holda na produkt.
            // Zalogowany: customer-id bierzemy z tokena; gość: generujemy serwerowy sessionId i zwracamy klientowi.
            // req.CustomerId jest ignorowane — poprzednio umożliwiało atakującemu podstawienie cudzego ID (SEC-A03).
            api.MapPost("/holds", [AllowAnonymous] async (
                CreateHoldRequest req,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                IBusinessHoursService businessHours,
                System.Security.Claims.ClaimsPrincipal user) =>
            {
                if (req == null) return Results.BadRequest("Brak danych");
                if (req.Quantity <= 0) return Results.BadRequest("Ilość musi być > 0");
                if (req.StartDateUtc >= req.EndDateUtc) return Results.BadRequest("Zakres dat niepoprawny");
                var nowUtc = DateTime.UtcNow;
                if (!PolishRentalTime.IsStartSafelyInFuture(req.StartDateUtc, nowUtc))
                    return Results.BadRequest("Data rozpoczęcia musi być co najmniej 2 minuty w przyszłości");

                await using var db = await dbFactory.CreateDbContextAsync();

                var product = await db.Products.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == req.ProductId && p.IsActive && p.Available && !p.Disabled && !p.IsDeleted);
                if (product == null) return Results.NotFound("Nie znaleziono produktu");

                var isDemoTenant = await db.Tenants.IgnoreQueryFilters()
                    .Where(t => t.Id == product.TenantId)
                    .Select(t => (bool?)t.IsDemo)
                    .FirstOrDefaultAsync();
                if (isDemoTenant != false) return Results.NotFound("Nie znaleziono produktu");

                var tid = product.TenantId;

                var businessWindow = await businessHours.ValidateRentalWindowAsync(
                    tid,
                    req.StartDateUtc,
                    req.EndDateUtc);
                if (!businessWindow.IsValid)
                {
                    return Results.BadRequest(new
                    {
                        error = businessWindow.Reason ?? "Wypożyczalnia jest zamknięta w wybranym terminie."
                    });
                }

                var ttl = Math.Clamp(req.TtlMinutes ?? 10, 5, 30);

                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

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

                var customerIdFromClaim = user.GetCustomerId();
                string? sessionId = null;
                if (customerIdFromClaim is null)
                {
                    // Guest path: accept client-provided sessionId only if it's long enough to resist guessing.
                    // Otherwise generate a cryptographically strong one server-side and return it.
                    if (!string.IsNullOrEmpty(req.SessionId) && req.SessionId.Length >= 24)
                    {
                        sessionId = req.SessionId;
                    }
                    else
                    {
                        sessionId = GenerateSessionId();
                    }
                }

                var hold = new ReservationHold
                {
                    Id = Guid.NewGuid(),
                    TenantId = tid,
                    ProductId = req.ProductId,
                    Quantity = req.Quantity,
                    StartDateUtc = req.StartDateUtc,
                    EndDateUtc = req.EndDateUtc,
                    CreatedAtUtc = nowUtc,
                    ExpiresAtUtc = nowUtc.AddMinutes(ttl),
                    CustomerId = customerIdFromClaim,
                    SessionId = sessionId
                };

                await db.ReservationHolds.AddAsync(hold);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Created($"/api/holds/{hold.Id}", new { hold.Id, hold.ExpiresAtUtc, SessionId = sessionId });
            });

            // Atomowe przedłużenie własnego holda. Nie tworzymy drugiego holda przed
            // usunięciem pierwszego, więc pełny stan magazynowy może być odświeżany.
            api.MapPost("/holds/{id:guid}/refresh", [AllowAnonymous] async (
                Guid id,
                string? sessionId,
                int? ttlMinutes,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ClaimsPrincipal user) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var hold = await db.ReservationHolds.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(h => h.Id == id);
                if (hold is null) return Results.NotFound();

                var customerId = user.GetCustomerId();
                var ownedByCustomer = customerId.HasValue && hold.CustomerId == customerId.Value;
                var ownedBySession = SessionIdEquals(hold.SessionId, sessionId);
                var (staffTenantId, isSuperAdmin) = ResolveTenantContext(user);
                var managedByStaff = user.IsAdmin() &&
                                     (isSuperAdmin || staffTenantId == hold.TenantId);
                if (!managedByStaff && !ownedByCustomer && !ownedBySession)
                    return Results.NotFound();

                var product = await db.Products.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == hold.ProductId && p.IsActive && p.Available && !p.Disabled && !p.IsDeleted);
                if (product is null) return Results.NotFound();

                var nowUtc = DateTime.UtcNow;
                if (!PolishRentalTime.IsStartSafelyInFuture(hold.StartDateUtc, nowUtc))
                    return Results.Conflict(new { error = "Termin wynajmu już się rozpoczął. Wybierz nowy termin." });
                var reservedQty = await db.RentalItems.IgnoreQueryFilters()
                    .Where(ri => ri.ProductId == hold.ProductId)
                    .Join(db.Rentals.IgnoreQueryFilters(), ri => ri.RentalId, r => r.Id, (ri, r) => new { ri, r })
                    .Where(x => x.r.Status != RentalStatus.Cancelled &&
                                x.r.EndDateUtc > hold.StartDateUtc &&
                                x.r.StartDateUtc < hold.EndDateUtc)
                    .SumAsync(x => (int?)x.ri.Quantity) ?? 0;
                var otherHoldsQty = await db.ReservationHolds.IgnoreQueryFilters()
                    .Where(h => h.Id != hold.Id && h.ProductId == hold.ProductId &&
                                h.ExpiresAtUtc > nowUtc &&
                                h.EndDateUtc > hold.StartDateUtc &&
                                h.StartDateUtc < hold.EndDateUtc)
                    .SumAsync(h => (int?)h.Quantity) ?? 0;

                if (reservedQty + otherHoldsQty + hold.Quantity > product.AvailableQuantity)
                    return Results.Conflict(new { error = "Wybrana ilość nie jest już dostępna." });

                hold.ExpiresAtUtc = nowUtc.AddMinutes(Math.Clamp(ttlMinutes ?? 10, 5, 30));
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Ok(new { hold.Id, hold.ExpiresAtUtc, hold.SessionId });
            });

            // Usunięcie (zwolnienie) holda — ownership: customer-id z tokena LUB sessionId z query (SEC-A03).
            api.MapDelete("/holds/{id:guid}", [AllowAnonymous] async (
                Guid id,
                string? sessionId,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                System.Security.Claims.ClaimsPrincipal user) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var hold = await db.ReservationHolds.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == id);
                if (hold == null) return Results.NotFound();

                var customerIdFromClaim = user.GetCustomerId();
                var ownedByCustomer = customerIdFromClaim.HasValue && hold.CustomerId == customerIdFromClaim.Value;
                var ownedBySession = SessionIdEquals(hold.SessionId, sessionId);
                var (staffTenantId, isSuperAdmin) = ResolveTenantContext(user);
                var managedByStaff = user.IsAdmin() &&
                                     (isSuperAdmin || staffTenantId == hold.TenantId);

                if (!managedByStaff && !ownedByCustomer && !ownedBySession)
                {
                    return Results.NotFound();
                }

                db.ReservationHolds.Remove(hold);
                await db.SaveChangesAsync();
                return Results.Ok();
            });

            return app;
        }

        private static void MapLegalEndpoints(RouteGroupBuilder api)
        {
            api.MapGet("/legal/info", [AllowAnonymous] (IConfiguration configuration) =>
            {
                static string? ReadPublicValue(IConfiguration config, string key)
                {
                    var value = config[key]?.Trim();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }

                var operatorName = ReadPublicValue(configuration, "Legal:OperatorName");
                var operatorAddress = ReadPublicValue(configuration, "Legal:OperatorAddress");
                var operatorNip = ReadPublicValue(configuration, "Legal:OperatorNip");
                var operatorKrs = ReadPublicValue(configuration, "Legal:OperatorKrs");
                var operatorEmail = ReadPublicValue(configuration, "Legal:OperatorEmail");
                var operatorPhone = ReadPublicValue(configuration, "Legal:OperatorPhone");

                var info = new LegalInfoDto
                {
                    ServiceName = ReadPublicValue(configuration, "Legal:ServiceName") ?? "RentSpot",
                    OperatorName = operatorName,
                    OperatorAddress = operatorAddress,
                    OperatorNip = operatorNip,
                    OperatorKrs = operatorKrs,
                    OperatorEmail = operatorEmail,
                    OperatorPhone = operatorPhone,
                    ComplaintsEmail = ReadPublicValue(configuration, "Legal:ComplaintsEmail") ?? operatorEmail,
                    PrivacyEmail = ReadPublicValue(configuration, "Legal:PrivacyEmail") ?? operatorEmail,
                    TermsVersion = LegalDocumentVersions.Terms,
                    PrivacyVersion = LegalDocumentVersions.Privacy,
                    EffectiveFromUtc = LegalDocumentVersions.EffectiveFromUtc,
                    IsOperatorDataComplete =
                        operatorName is not null &&
                        operatorAddress is not null &&
                        operatorEmail is not null &&
                        operatorPhone is not null &&
                        (operatorNip is not null || operatorKrs is not null)
                };

                return Results.Ok(info);
            });
        }

        private static void MapAuthEndpoints(RouteGroupBuilder api)
        {
            var auth = api.MapGroup("/auth");

            auth.MapGet("/providers", [AllowAnonymous] async (
                SignInManager<ApplicationUser> signInManager) =>
            {
                var schemes = await signInManager.GetExternalAuthenticationSchemesAsync();
                return Results.Ok(new
                {
                    google = schemes.Any(scheme =>
                        string.Equals(scheme.Name, "Google", StringComparison.OrdinalIgnoreCase))
                });
            }).RequireRateLimiting("api");

            // Rejestracja tworzy konto i profil, ale pełna sesja Client powstaje dopiero
            // po potwierdzeniu skrzynki. Gość nadal może korzystać z osobnego guest flow.
            auth.MapPost("/register", [AllowAnonymous] async (
                RegisterRequest request,
                UserManager<ApplicationUser> userManager,
                CustomerIdentityService customerIdentity,
                IEmailSender<ApplicationUser> emailSender,
                IConfiguration configuration,
                ILoggerFactory loggerFactory) =>
            {
                if (!string.Equals(
                        request.AcceptedTermsVersion,
                        LegalDocumentVersions.Terms,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        request.AcknowledgedPrivacyVersion,
                        LegalDocumentVersions.Privacy,
                        StringComparison.Ordinal))
                {
                    return Results.BadRequest(new
                    {
                        error = "Przed rejestracją zaakceptuj aktualny regulamin i potwierdź zapoznanie się z polityką prywatności."
                    });
                }

                var validationError = PublicAuthInputValidator.ValidateRegister(
                    request.Email,
                    request.Password,
                    request.FullName,
                    request.PhoneNumber,
                    request.DocumentNumber);
                if (validationError is not null)
                    return Results.BadRequest(new { error = validationError });

                PublicAuthInputValidator.TryNormalizeEmail(request.Email, out var normalizedEmail);

                // Konto klienta publicznego jest globalne. Wybór wypożyczalni w katalogu
                // jest filtrem zakupowym i nie może przypinać tożsamości do jednego tenanta.
                Guid? tenantId = null;

                var existingUser = await userManager.FindByEmailAsync(normalizedEmail);
                if (existingUser != null)
                {
                    return Results.BadRequest(new { error = "Email już jest zarejestrowany" });
                }

                var user = new ApplicationUser
                {
                    UserName = normalizedEmail,
                    Email = normalizedEmail,
                    TenantId = tenantId,
                    EmailConfirmed = false,
                    AcceptedTermsVersion = LegalDocumentVersions.Terms,
                    AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy,
                    LegalAcceptedAtUtc = DateTime.UtcNow
                };

                var result = await userManager.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    return Results.BadRequest(new { error = errors });
                }

                var roleResult = await userManager.AddToRoleAsync(user, "Client");
                if (!roleResult.Succeeded)
                {
                    await userManager.DeleteAsync(user);
                    return Results.Json(
                        new { error = "Nie udało się utworzyć konta. Spróbuj ponownie." },
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                Customer customer;
                try
                {
                    customer = await customerIdentity.GetOrCreateAsync(
                        user,
                        request.FullName,
                        request.PhoneNumber,
                        request.DocumentNumber);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger("ClientRegistration")
                        .LogError(ex, "Nie udało się utworzyć profilu klienta dla użytkownika {UserId}.", user.Id);
                    await userManager.DeleteAsync(user);
                    return Results.Json(
                        new { error = "Nie udało się utworzyć konta. Spróbuj ponownie." },
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                await SendEmailConfirmationLinkBestEffortAsync(
                    user,
                    normalizedEmail,
                    userManager,
                    emailSender,
                    configuration,
                    loggerFactory.CreateLogger("ClientRegistration"));

                return Results.Ok(new
                {
                    ExpiresIn = 0,
                    User = new
                    {
                        Id = user.Id,
                        Email = user.Email,
                        TenantId = tenantId,
                        CustomerId = customer.Id,
                        EmailConfirmed = false
                    },
                    EmailConfirmationRequired = true
                });
            }).RequireRateLimiting("auth");

            // Odpowiedź jest celowo identyczna dla konta nieistniejącego, już
            // potwierdzonego i oczekującego, aby endpoint nie ujawniał rejestru kont.
            auth.MapPost("/resend-confirmation", [AllowAnonymous] async (
                ResendEmailConfirmationRequest request,
                UserManager<ApplicationUser> userManager,
                IEmailSender<ApplicationUser> emailSender,
                IConfiguration configuration,
                ILoggerFactory loggerFactory) =>
            {
                if (PublicAuthInputValidator.TryNormalizeEmail(request.Email, out var normalizedEmail))
                {
                    var user = await userManager.FindByEmailAsync(normalizedEmail);
                    if (user is not null && !await userManager.IsEmailConfirmedAsync(user))
                    {
                        await SendEmailConfirmationLinkBestEffortAsync(
                            user,
                            normalizedEmail,
                            userManager,
                            emailSender,
                            configuration,
                            loggerFactory.CreateLogger("ClientEmailConfirmation"));
                    }
                }

                return Results.Accepted(value: new
                {
                    message = "Jeśli konto oczekuje na potwierdzenie, wysłaliśmy nowy link."
                });
            }).RequireRateLimiting("auth");

            // Login endpoint — writes HttpOnly access-token cookie (SEC-009).
            auth.MapPost("/login", [AllowAnonymous] async (
                LoginRequest request,
                UserManager<ApplicationUser> userManager,
                SignInManager<ApplicationUser> signInManager,
                JwtTokenService jwt,
                CustomerIdentityService customerIdentity,
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

                if (!await userManager.IsEmailConfirmedAsync(user))
                {
                    if (await userManager.IsLockedOutAsync(user))
                        return Results.BadRequest(new { error = "Konto zablokowane" });

                    // Status potwierdzenia ujawniamy dopiero po poprawnym haśle.
                    // SignInManager wykonuje RequireConfirmedAccount przed weryfikacją
                    // hasła, więc dla tego jednego stanu sprawdzamy je jawnie.
                    if (!await userManager.CheckPasswordAsync(user, request.Password))
                    {
                        if (userManager.SupportsUserLockout)
                            await userManager.AccessFailedAsync(user);
                        return Results.BadRequest(new { error = "Nieprawidłowy email lub hasło" });
                    }

                    if (userManager.SupportsUserLockout)
                        await userManager.ResetAccessFailedCountAsync(user);

                    return Results.Json(
                        new
                        {
                            error = "Potwierdź adres e-mail przed zalogowaniem.",
                            code = "email_confirmation_required"
                        },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
                if (!result.Succeeded)
                {
                    if (result.IsLockedOut)
                        return Results.BadRequest(new { error = "Konto zablokowane" });
                    return Results.BadRequest(new { error = "Nieprawidłowy email lub hasło" });
                }

                var roles = await userManager.GetRolesAsync(user);
                if (!CanUseClientApplication(roles))
                {
                    DeleteAccessTokenCookie(httpContext);
                    await signInManager.SignOutAsync();
                    return Results.Json(
                        new { error = "To konto nie ma dostępu do aplikacji klienta.", code = "client_access_denied" },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                var customer = await customerIdentity.GetOrCreateAsync(user);

                var token = jwt.CreateUserToken(user, user.TenantId ?? Guid.Empty, roles, customer.Id);
                WriteAccessTokenCookie(httpContext, token.AccessToken, token.ExpiresAtUtc);

                return Results.Ok(new
                {
                    ExpiresIn = (int)(token.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds,
                    User = new
                    {
                        Id = user.Id,
                        Email = user.Email,
                        TenantId = user.TenantId,
                        CustomerId = customer.Id
                    }
                });
            }).RequireRateLimiting("auth");

            // Krótki JWT w HttpOnly cookie może wygasnąć podczas dłuższej sesji WASM.
            // Ważna sesyjna cookie Identity po ponownym sprawdzeniu konta wystawia nowy
            // token. Owner i SuperAdmin mogą używać tej samej aplikacji klienckiej w trybie
            // podglądu/testu; guest-session celowo nie ma tej ścieżki i pozostaje ograniczona do 48 h.
            auth.MapPost("/refresh", [AllowAnonymous] async (
                UserManager<ApplicationUser> userManager,
                JwtTokenService jwt,
                CustomerIdentityService customerIdentity,
                HttpContext httpContext) =>
            {
                // Cookie handler redirects its challenge to /Account/Login. Refresh jest jednak
                // endpointem API, więc brak sesji musi kończyć się czystym 401 bez HTML/redirectu.
                // Uwierzytelniamy jawnie wyłącznie Identity cookie: sam JWT klienta nie może
                // przedłużać własnej sesji po wygaśnięciu lub usunięciu cookie Identity.
                var identityAuthentication = await httpContext.AuthenticateAsync(
                    IdentityConstants.ApplicationScheme);
                if (!identityAuthentication.Succeeded ||
                    identityAuthentication.Principal?.Identity?.IsAuthenticated != true)
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.GetUserAsync(identityAuthentication.Principal);
                if (user is null || !await userManager.IsEmailConfirmedAsync(user))
                    return Results.Forbid();

                var roles = await userManager.GetRolesAsync(user);
                if (!CanUseClientApplication(roles))
                    return Results.Forbid();

                var customer = await customerIdentity.GetOrCreateAsync(user);
                var token = jwt.CreateUserToken(
                    user,
                    user.TenantId ?? Guid.Empty,
                    roles,
                    customer.Id);
                WriteAccessTokenCookie(httpContext, token.AccessToken, token.ExpiresAtUtc);

                return Results.Ok(new
                {
                    ExpiresIn = (int)(token.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds
                });
            }).RequireRateLimiting("session");

            // Jawny hand-off z panelu partnera do bundlowanego WASM. Najpierw wystawiamy
            // klientowy JWT w HttpOnly cookie, a dopiero potem przekierowujemy do /_client,
            // dzięki czemu aplikacja nie odbija Ownera/SuperAdmina z powrotem do /Account/Login.
            auth.MapGet("/client-preview", [Authorize(
                AuthenticationSchemes = "Identity.Application",
                Roles = RoleNames.Owner + "," + RoleNames.SuperAdmin)] async (
                string? returnUrl,
                UserManager<ApplicationUser> userManager,
                JwtTokenService jwt,
                CustomerIdentityService customerIdentity,
                HttpContext httpContext) =>
            {
                var user = await userManager.GetUserAsync(httpContext.User);
                if (user is null || !await userManager.IsEmailConfirmedAsync(user))
                    return Results.Forbid();

                var roles = await userManager.GetRolesAsync(user);
                var customer = await customerIdentity.GetOrCreateAsync(user);
                var token = jwt.CreateUserToken(
                    user,
                    user.TenantId ?? Guid.Empty,
                    roles,
                    customer.Id);
                WriteAccessTokenCookie(httpContext, token.AccessToken, token.ExpiresAtUtc);

                return Results.Redirect(ResolveClientReturnUrl(returnUrl));
            }).RequireRateLimiting("session");

            // Guest session — anonymous checkout path: always creates an isolated Customer
            // and issues a short-lived JWT bound only to that new customer-id.
            auth.MapPost("/guest-session", [AllowAnonymous] async (
                GuestSessionRequest request,
                UserManager<ApplicationUser> userManager,
                JwtTokenService jwt,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var validationError = PublicAuthInputValidator.ValidateGuestSession(
                    request.Email,
                    request.FullName,
                    request.PhoneNumber,
                    request.Address,
                    request.DocumentNumber,
                    request.Notes);
                if (validationError is not null)
                    return Results.BadRequest(new { error = validationError });

                PublicAuthInputValidator.TryNormalizeEmail(request.Email, out var normalizedEmail);

                // If user already has a registered account, force login (don't let attacker short-circuit with guest flow).
                var existingUser = await userManager.FindByEmailAsync(normalizedEmail);
                if (existingUser != null)
                {
                    return Results.Conflict(new { error = "Masz już konto — zaloguj się, aby kontynuować." });
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                // Nigdy nie wznawiamy profilu po samym e-mailu. Adres nie jest tu
                // zweryfikowany, więc ponowne użycie rekordu ujawniłoby historię i umowy.
                // Profil gościa jest globalny; X-Tenant-Id jest niezaufanym filtrem katalogu,
                // a nie źródłem zakresu tożsamości.
                var customer = new Customer
                {
                    Id = Guid.NewGuid(),
                    TenantId = Guid.Empty,
                    FullName = request.FullName.Trim(),
                    Email = normalizedEmail,
                    PhoneNumber = request.PhoneNumber?.Trim(),
                    Address = request.Address?.Trim(),
                    DocumentNumber = request.DocumentNumber?.Trim(),
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Customers.Add(customer);
                await db.SaveChangesAsync(ct);

                var token = jwt.CreateGuestToken(customer.Id, customer.TenantId, customer.Email ?? normalizedEmail);
                WriteAccessTokenCookie(httpContext, token.AccessToken, token.ExpiresAtUtc);

                return Results.Ok(new
                {
                    ExpiresIn = (int)(token.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds,
                    CustomerId = customer.Id,
                    Email = customer.Email,
                    FullName = customer.FullName
                });
            }).RequireRateLimiting("auth");

            // Gość nie ma hasła ani odnawialnej sesji. Po wygaśnięciu cookie może
            // odzyskać dostęp wyłącznie przez jednorazowy link wysłany na adres użyty
            // w zamówieniu i po podaniu publicznego numeru tego zamówienia.
            auth.MapPost("/guest-order-access/request", [AllowAnonymous] async (
                GuestOrderAccessRequest request,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                SportRental.Admin.Services.Email.IEmailSender emailSender,
                IConfiguration configuration,
                IWebHostEnvironment environment,
                HttpContext httpContext,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                if (!PublicAuthInputValidator.TryNormalizeEmail(request.Email, out var normalizedEmail))
                    return Results.BadRequest(new { error = "Podaj poprawny adres e-mail." });

                var orderNumber = request.OrderNumber?.Trim().ToUpperInvariant() ?? string.Empty;
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        orderNumber,
                        "^RS-[0-9]{8}-[A-F0-9]{8}$",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                {
                    return Results.BadRequest(new { error = "Podaj poprawny numer zamówienia, np. RS-20260710-ABC12345." });
                }

                const string genericMessage =
                    "Jeżeli dane pasują do zamówienia gościa, wysłaliśmy jednorazowy link dostępu.";
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var order = await db.MarketplaceOrders
                    .AsNoTracking()
                    .Include(candidate => candidate.Customer)
                    .Include(candidate => candidate.CheckoutSession)
                    .FirstOrDefaultAsync(candidate => candidate.OrderNumber == orderNumber, ct);
                var immutableOrderEmail = order?.CustomerEmailSnapshot;
                if (string.IsNullOrWhiteSpace(immutableOrderEmail) &&
                    !string.IsNullOrWhiteSpace(order?.CheckoutSession?.PayloadJson))
                {
                    try
                    {
                        immutableOrderEmail = System.Text.Json.JsonSerializer
                            .Deserialize<Payments.CheckoutRentalPayload>(order.CheckoutSession.PayloadJson)
                            ?.Customer.Email;
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        immutableOrderEmail = null;
                    }
                }
                if (order?.Customer is null ||
                    !string.Equals(
                        immutableOrderEmail?.Trim(),
                        normalizedEmail,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Accepted(value: new { message = genericMessage });
                }

                var linkedIdentityAccountExists = await db.UserClaims.AnyAsync(
                    claim => claim.ClaimType == AuthClaims.CustomerId &&
                             claim.ClaimValue == order.CustomerId.ToString(),
                    ct);
                if (linkedIdentityAccountExists)
                {
                    // Nie obniżamy zabezpieczeń konta Identity do sesji gościa.
                    // Właściciel konta korzysta ze zwykłego logowania/resetu hasła.
                    return Results.Accepted(value: new { message = genericMessage });
                }

                if (!ClientAppUrlResolver.TryResolveSecurityBaseUrl(
                        configuration,
                        environment,
                        out var clientBaseUrl))
                {
                    loggerFactory.CreateLogger("GuestOrderAccess")
                        .LogError(
                            "Nie wysłano linku odzyskiwania: ustaw poprawny HTTPS ClientApp:PublicBaseUrl lub Admin:PublicBaseUrl");
                    return Results.Accepted(value: new { message = genericMessage });
                }

                var nowUtc = DateTime.UtcNow;
                var activeTokens = await db.GuestOrderAccessTokens
                    .Where(token => token.CustomerId == order.CustomerId &&
                                    token.UsedAtUtc == null &&
                                    token.ExpiresAtUtc > nowUtc)
                    .ToListAsync(ct);
                foreach (var activeToken in activeTokens)
                    activeToken.UsedAtUtc = nowUtc;

                var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                var tokenHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
                var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
                db.GuestOrderAccessTokens.Add(new GuestOrderAccessToken
                {
                    Id = Guid.NewGuid(),
                    CustomerId = order.CustomerId,
                    MarketplaceOrderId = order.Id,
                    TokenHash = tokenHash,
                    CreatedAtUtc = nowUtc,
                    ExpiresAtUtc = nowUtc.AddMinutes(20),
                    RequestedFromIp = remoteIp is { Length: > 64 } ? remoteIp[..64] : remoteIp
                });
                await db.SaveChangesAsync(ct);

                var accessUrl = $"{clientBaseUrl}/guest-access?token={Uri.EscapeDataString(rawToken)}";
                var encodedUrl = HtmlEncoder.Default.Encode(accessUrl);
                var encodedName = HtmlEncoder.Default.Encode(order.Customer.FullName ?? "Kliencie");
                var encodedOrderNumber = HtmlEncoder.Default.Encode(order.OrderNumber);

                try
                {
                    await emailSender.SendEmailAsync(
                        normalizedEmail,
                        $"Dostęp do zamówienia {order.OrderNumber}",
                        $"""
                        <p>Dzień dobry {encodedName},</p>
                        <p>otrzymaliśmy prośbę o dostęp do zamówienia <strong>{encodedOrderNumber}</strong>.</p>
                        <p><a href="{encodedUrl}">Otwórz moje rezerwacje</a></p>
                        <p>Link jest jednorazowy i wygaśnie po 20 minutach. Jeżeli to nie Ty wysłałeś prośbę, zignoruj tę wiadomość.</p>
                        """);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger("GuestOrderAccess")
                        .LogError(ex, "Nie udało się wysłać linku odzyskiwania dla zamówienia {OrderId}", order.Id);
                }

                return Results.Accepted(value: new { message = genericMessage });
            }).RequireRateLimiting("auth");

            auth.MapPost("/guest-order-access/redeem", [AllowAnonymous] async (
                GuestOrderAccessRedeemRequest request,
                IDbContextFactory<ApplicationDbContext> dbFactory,
                JwtTokenService jwt,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var rawToken = request.Token?.Trim();
                if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length is < 32 or > 128)
                    return Results.BadRequest(new { error = "Link dostępu jest nieprawidłowy lub wygasł." });

                var tokenHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);
                var accessToken = await db.GuestOrderAccessTokens
                    .Include(token => token.Customer)
                    .Include(token => token.MarketplaceOrder)
                    .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, ct);
                var nowUtc = DateTime.UtcNow;
                if (accessToken?.Customer is null ||
                    accessToken.MarketplaceOrder is null ||
                    accessToken.MarketplaceOrder.CustomerId != accessToken.CustomerId ||
                    accessToken.UsedAtUtc.HasValue ||
                    accessToken.ExpiresAtUtc <= nowUtc)
                {
                    return Results.BadRequest(new { error = "Link dostępu jest nieprawidłowy lub wygasł." });
                }

                var linkedIdentityAccountExists = await db.UserClaims.AnyAsync(
                    claim => claim.ClaimType == AuthClaims.CustomerId &&
                             claim.ClaimValue == accessToken.CustomerId.ToString(),
                    ct);
                if (linkedIdentityAccountExists)
                {
                    accessToken.UsedAtUtc = nowUtc;
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return Results.BadRequest(new { error = "Link dostępu jest nieprawidłowy lub wygasł." });
                }

                accessToken.UsedAtUtc = nowUtc;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                var customer = accessToken.Customer;
                var guestToken = jwt.CreateGuestToken(
                    customer.Id,
                    customer.TenantId,
                    customer.Email ?? string.Empty);
                WriteAccessTokenCookie(httpContext, guestToken.AccessToken, guestToken.ExpiresAtUtc);

                return Results.Ok(new
                {
                    ExpiresIn = (int)(guestToken.ExpiresAtUtc - nowUtc).TotalSeconds,
                    CustomerId = customer.Id,
                    Email = customer.Email ?? string.Empty,
                    FullName = customer.FullName ?? "Klient"
                });
            }).RequireRateLimiting("auth");

            // SEC-009: logout — usuwa HttpOnly cookie z tokenem.
            auth.MapPost("/logout", [AllowAnonymous] async (
                HttpContext httpContext,
                SignInManager<ApplicationUser> signInManager) =>
            {
                DeleteAccessTokenCookie(httpContext);
                await signInManager.SignOutAsync();
                return Results.Ok();
            });

            // Kończy logowanie zewnętrzne rozpoczęte z WASM. Identity obsługuje handshake
            // Google, a ten endpoint wystawia równoległą, HttpOnly sesję JWT dla /api/*.
            auth.MapGet("/external-complete", [Authorize(AuthenticationSchemes = "Identity.Application")] async (
                HttpContext httpContext,
                UserManager<ApplicationUser> userManager,
                SignInManager<ApplicationUser> signInManager,
                JwtTokenService jwt,
                CustomerIdentityService customerIdentity,
                string? returnUrl) =>
            {
                var user = await userManager.GetUserAsync(httpContext.User);
                if (user is null || string.IsNullOrWhiteSpace(user.Email))
                    return Results.Redirect("/_client/login");

                var roles = await userManager.GetRolesAsync(user);
                if (roles.Count == 0)
                {
                    await userManager.AddToRoleAsync(user, RoleNames.Client);
                    roles = await userManager.GetRolesAsync(user);
                }

                if (!CanUseClientApplication(roles))
                {
                    DeleteAccessTokenCookie(httpContext);
                    await signInManager.SignOutAsync();
                    return Results.Redirect("/_client/login?error=client_access_denied");
                }

                var customer = await customerIdentity.GetOrCreateAsync(
                    user,
                    httpContext.User.FindFirstValue(ClaimTypes.Name));

                var token = jwt.CreateUserToken(user, user.TenantId ?? Guid.Empty, roles, customer.Id);
                WriteAccessTokenCookie(httpContext, token.AccessToken, token.ExpiresAtUtc);

                return Results.Redirect(ResolveClientReturnUrl(returnUrl));
            }).RequireRateLimiting("auth");

            // SEC-009: /auth/me — WASM pyta tu o stan uwierzytelnienia przy starcie
            // (bo token jest teraz w HttpOnly cookie i niedostępny z JS).
            // Tylko JwtBearer — żeby brak auth zwracał 401 (Identity.Application czaruje 302 do /Account/Login).
            auth.MapGet("/me", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] (ClaimsPrincipal user) =>
            {
                var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var email = user.FindFirst(ClaimTypes.Email)?.Value;
                var tenantIdStr = user.FindFirst("tenant-id")?.Value;
                var customerIdStr = user.FindFirst("customer-id")?.Value;
                var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

                return Results.Ok(new
                {
                    Id = id,
                    Email = email,
                    TenantId = Guid.TryParse(tenantIdStr, out var tid) ? tid : (Guid?)null,
                    CustomerId = Guid.TryParse(customerIdStr, out var cid) ? cid : (Guid?)null,
                    Roles = roles
                });
            });
        }

        public sealed record GuestSessionRequest(
            string Email,
            string FullName,
            string? PhoneNumber,
            string? Address,
            string? DocumentNumber,
            string? Notes);

        public sealed record GuestOrderAccessRequest(string Email, string OrderNumber);
        public sealed record GuestOrderAccessRedeemRequest(string Token);

        // Customer endpoints for WASM client
        private static void MapCustomerEndpoints(IEndpointRouteBuilder api)
        {
            // GET /api/customers/by-email?email=xxx
            // Auth: tylko własny email (zapobiega enumeracji PII — SEC-A03) albo admin.
            api.MapGet("/customers/by-email", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                System.Security.Claims.ClaimsPrincipal user,
                string? email,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(email))
                {
                    return Results.BadRequest(new { error = "Email query parameter is required." });
                }

                var normalizedEmail = email.Trim().ToLower();
                var ownEmail = user.FindFirst(ClaimTypes.Email)?.Value?.Trim().ToLower();

                if (!user.IsAdmin() && !string.Equals(normalizedEmail, ownEmail, StringComparison.Ordinal))
                {
                    return Results.NotFound();
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var (tenantId, isSuperAdmin) = ResolveTenantContext(user);

                IQueryable<Customer> query;
                if (!user.IsAdmin())
                {
                    var ownCustomerId = user.GetCustomerId();
                    if (!ownCustomerId.HasValue)
                        return Results.NotFound();

                    query = db.Customers.IgnoreQueryFilters()
                        .Where(c => c.Id == ownCustomerId.Value);
                }
                else
                {
                    if (!isSuperAdmin && tenantId == Guid.Empty)
                        return Results.Forbid();

                    query = db.Customers.IgnoreQueryFilters()
                        .Where(c => c.Email != null && c.Email.ToLower() == normalizedEmail);
                    if (tenantId != Guid.Empty)
                    {
                        query = query.Where(c => c.TenantId == tenantId ||
                            db.Rentals.IgnoreQueryFilters()
                                .Any(r => r.CustomerId == c.Id && r.TenantId == tenantId));
                    }
                }

                var customer = await query.FirstOrDefaultAsync(ct);
                return customer is null
                    ? Results.NotFound()
                    : Results.Ok(ToCustomerDto(customer, includeNotes: user.IsAdmin()));
            });

            // GET /api/customers/{id}
            // Auth: tylko własny customer-id (ownership) albo admin (SEC-A03).
            api.MapGet("/customers/{id:guid}", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                System.Security.Claims.ClaimsPrincipal user,
                Guid id,
                CancellationToken ct) =>
            {
                var customerIdFromClaim = user.GetCustomerId();
                if (!user.IsAdmin() && customerIdFromClaim != id)
                {
                    return Results.NotFound();
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var (tenantId, isSuperAdmin) = ResolveTenantContext(user);

                if (user.IsAdmin() && !isSuperAdmin && tenantId == Guid.Empty)
                    return Results.Forbid();

                var query = db.Customers.IgnoreQueryFilters().Where(c => c.Id == id);
                if (user.IsAdmin() && !isSuperAdmin)
                {
                    query = query.Where(c => c.TenantId == tenantId ||
                        db.Rentals.IgnoreQueryFilters()
                            .Any(r => r.CustomerId == c.Id && r.TenantId == tenantId));
                }

                var customer = await query.FirstOrDefaultAsync(ct);
                return customer is null
                    ? Results.NotFound()
                    : Results.Ok(ToCustomerDto(customer, includeNotes: user.IsAdmin()));
            });

            // POST /api/customers
            // Tworzenie klientów przez panel jest operacją pracowniczą. Publiczny checkout
            // zakłada bezpieczną sesję przez /api/auth/guest-session.
            api.MapPost("/customers", [Authorize(
                AuthenticationSchemes = ApiAuthSchemes,
                Roles = "Owner,Employee,SuperAdmin")] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                SharedModels.CreateCustomerRequest req,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                if (tenantId == Guid.Empty)
                    return Results.BadRequest(new { error = "Wybierz tenant przed utworzeniem klienta." });

                var validationResults = new List<ValidationResult>();
                if (!Validator.TryValidateObject(req, new ValidationContext(req), validationResults, true))
                {
                    return Results.ValidationProblem(validationResults
                        .SelectMany(result => result.MemberNames.Select(member => new
                        {
                            Member = member,
                            Message = result.ErrorMessage ?? "Nieprawidłowa wartość."
                        }))
                        .GroupBy(error => error.Member)
                        .ToDictionary(group => group.Key, group =>
                            group.Select(error => error.Message).Distinct().ToArray()));
                }

                var normalizedEmail = req.Email?.Trim();

                if (!string.IsNullOrEmpty(normalizedEmail))
                {
                    var query = db.Customers.IgnoreQueryFilters()
                        .Where(c => c.Email != null && c.Email.ToLower() == normalizedEmail.ToLower());

                    if (tenantId != Guid.Empty)
                    {
                        query = query.Where(c => c.TenantId == tenantId);
                    }

                    if (await query.AnyAsync(ct))
                    {
                        return Results.Conflict(new { error = "Customer with the provided email already exists." });
                    }
                }

                var customer = new Customer
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    FullName = req.FullName.Trim(),
                    Email = normalizedEmail,
                    PhoneNumber = req.PhoneNumber?.Trim(),
                    Address = req.Address?.Trim(),
                    DocumentNumber = req.DocumentNumber?.Trim(),
                    // Publiczny klient nie może wpisywać wewnętrznych notatek CRM.
                    Notes = null,
                    CreatedAtUtc = DateTime.UtcNow
                };

                await db.Customers.AddAsync(customer, ct);
                await db.SaveChangesAsync(ct);

                return Results.Created($"/api/customers/{customer.Id}", ToCustomerDto(customer, includeNotes: true));
            });

            // PUT /api/customers/{id}
            // Auth: tylko własny customer-id albo admin (SEC-A03).
            api.MapPut("/customers/{id:guid}", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                System.Security.Claims.ClaimsPrincipal user,
                Guid id,
                SharedModels.CreateCustomerRequest req,
                CancellationToken ct) =>
            {
                var customerIdFromClaim = user.GetCustomerId();
                if (!user.IsAdmin() && customerIdFromClaim != id)
                {
                    return Results.NotFound();
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;

                var validationResults = new List<ValidationResult>();
                if (!Validator.TryValidateObject(req, new ValidationContext(req), validationResults, true))
                {
                    return Results.ValidationProblem(validationResults
                        .SelectMany(result => result.MemberNames.Select(member => new
                        {
                            Member = member,
                            Message = result.ErrorMessage ?? "Nieprawidłowa wartość."
                        }))
                        .GroupBy(error => error.Member)
                        .ToDictionary(group => group.Key, group =>
                            group.Select(error => error.Message).Distinct().ToArray()));
                }

                var customer = await db.Customers.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == id, ct);

                if (customer is null)
                {
                    return Results.NotFound();
                }

                if (user.IsAdmin() && !user.IsInRole(SportRental.Shared.Identity.RoleNames.SuperAdmin))
                {
                    if (tenantId == Guid.Empty)
                        return Results.Forbid();

                    // Wynajem daje tenantowi prawo wglądu do danych potrzebnych do
                    // realizacji usługi, ale nie prawo zmiany globalnej tożsamości
                    // klienta marketplace ani profilu należącego do innego tenanta.
                    if (customer.TenantId != tenantId)
                        return Results.NotFound();

                    var isSharedIdentity = await db.MarketplaceOrders
                        .AnyAsync(order => order.CustomerId == customer.Id, ct) ||
                        await db.Rentals.IgnoreQueryFilters()
                            .AnyAsync(rental => rental.CustomerId == customer.Id && rental.TenantId != tenantId, ct);
                    if (isSharedIdentity)
                        return Results.NotFound();
                }

                var normalizedEmail = req.Email?.Trim();
                var emailChanged = !string.Equals(customer.Email?.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase);

                if (!user.IsAdmin() && emailChanged)
                {
                    return Results.BadRequest(new
                    {
                        error = "Adres e-mail konta można zmienić wyłącznie po ponownej weryfikacji."
                    });
                }

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

                customer.FullName = req.FullName.Trim();
                customer.Email = normalizedEmail;
                customer.PhoneNumber = req.PhoneNumber?.Trim();
                customer.Address = req.Address?.Trim();
                customer.DocumentNumber = req.DocumentNumber?.Trim();
                if (user.IsAdmin())
                    customer.Notes = req.Notes;

                await db.SaveChangesAsync(ct);

                return Results.Ok(ToCustomerDto(customer, includeNotes: user.IsAdmin()));
            });
        }

        private static SharedModels.CustomerDto ToCustomerDto(Customer c, bool includeNotes = false) => new()
        {
            Id = c.Id,
            FullName = c.FullName,
            Email = c.Email ?? string.Empty,
            PhoneNumber = c.PhoneNumber ?? string.Empty,
            Address = c.Address,
            DocumentNumber = c.DocumentNumber,
            // Notatki są wewnętrznym polem CRM i nie mogą trafiać do publicznego klienta.
            Notes = includeNotes ? c.Notes : null
        };

        // Opinie klientów po zakończonym wynajmie.
        // - POST wymaga auth: ustawia opinię tylko klient który faktycznie wypożyczył (Rental.CustomerId)
        //   i tylko gdy Rental.Status = Completed. Jeden review per Rental (unique index).
        // - GET publiczny (anonymous), filtrowany po tenantId z routy. Ukryte przez moderację (IsHidden=true) nie są zwracane.
        private static void MapReviewEndpoints(IEndpointRouteBuilder api)
        {
            // POST /api/reviews — klient wystawia opinię dla swojego zakończonego wynajmu.
            api.MapPost("/reviews", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                ClaimsPrincipal user,
                SharedModels.CreateRentalReviewRequest req,
                CancellationToken ct) =>
            {
                var customerId = user.GetCustomerId();
                if (customerId is null)
                {
                    return Results.Forbid();
                }

                if (req.QualityScore is < 0 or > 10 ||
                    req.PriceScore is < 0 or > 10 ||
                    req.ServiceScore is < 0 or > 10)
                {
                    return Results.BadRequest(new { error = "Scores must be between 0 and 10." });
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var rental = await db.Rentals.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Id == req.RentalId, ct);

                if (rental is null || rental.CustomerId != customerId.Value)
                {
                    return Results.NotFound();
                }

                if (rental.Status != RentalStatus.Completed)
                {
                    return Results.BadRequest(new { error = "Only completed rentals can be reviewed." });
                }

                var alreadyExists = await db.RentalReviews.IgnoreQueryFilters()
                    .AnyAsync(rr => rr.RentalId == rental.Id, ct);

                if (alreadyExists)
                {
                    return Results.Conflict(new { error = "Review for this rental already exists." });
                }

                // Walidacja per-item reviews (opcjonalnych)
                List<RentalItemReview>? itemReviews = null;
                if (req.ItemReviews is { Count: > 0 })
                {
                    var rentalItemIds = await db.RentalItems.IgnoreQueryFilters()
                        .Where(ri => ri.RentalId == rental.Id)
                        .Select(ri => new { ri.Id, ri.ProductId })
                        .ToListAsync(ct);
                    var validItemIds = rentalItemIds.ToDictionary(x => x.Id, x => x.ProductId);

                    var seen = new HashSet<Guid>();
                    itemReviews = new List<RentalItemReview>();
                    foreach (var ir in req.ItemReviews)
                    {
                        if (ir.Rating is < 0 or > 10)
                            return Results.BadRequest(new { error = $"Item rating must be 0-10 (got {ir.Rating})." });
                        if (!validItemIds.TryGetValue(ir.RentalItemId, out var productId))
                            return Results.BadRequest(new { error = $"RentalItem {ir.RentalItemId} does not belong to this rental." });
                        if (!seen.Add(ir.RentalItemId))
                            return Results.BadRequest(new { error = "Duplicate per-item review." });

                        itemReviews.Add(new RentalItemReview
                        {
                            Id = Guid.NewGuid(),
                            RentalItemId = ir.RentalItemId,
                            ProductId = productId,
                            Rating = ir.Rating,
                            Comment = string.IsNullOrWhiteSpace(ir.Comment) ? null : ir.Comment.Trim(),
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }
                }

                var review = new RentalReview
                {
                    Id = Guid.NewGuid(),
                    TenantId = rental.TenantId,
                    RentalId = rental.Id,
                    CustomerId = customerId.Value,
                    QualityScore = req.QualityScore,
                    PriceScore = req.PriceScore,
                    ServiceScore = req.ServiceScore,
                    Comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim(),
                    IsHidden = false,
                    CreatedAtUtc = DateTime.UtcNow
                };

                if (itemReviews is not null)
                {
                    foreach (var ir in itemReviews)
                    {
                        ir.RentalReviewId = review.Id;
                        review.ItemReviews.Add(ir);
                    }
                }

                await db.RentalReviews.AddAsync(review, ct);
                await db.SaveChangesAsync(ct);

                return Results.Created($"/api/reviews/{review.Id}", ToReviewDto(review, customerAnonymized: null));
            });

            // GET /api/tenants/{tenantId}/reviews — publiczna lista opinii dla tenanta.
            api.MapGet("/tenants/{tenantId:guid}/reviews", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                Guid tenantId,
                int? page,
                int? pageSize,
                CancellationToken ct) =>
            {
                var take = Math.Clamp(pageSize ?? 20, 1, 100);
                var skip = Math.Max(0, ((page ?? 1) - 1) * take);

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var query = db.RentalReviews.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(r => r.TenantId == tenantId && !r.IsHidden);

                var items = await query
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .Skip(skip).Take(take)
                    .Select(r => new
                    {
                        r.Id,
                        r.RentalId,
                        CustomerName = r.Customer != null ? r.Customer.FullName : "Klient",
                        r.QualityScore,
                        r.PriceScore,
                        r.ServiceScore,
                        r.Comment,
                        r.CreatedAtUtc,
                        ItemReviews = r.ItemReviews.Select(ir => new SharedModels.RentalItemReviewDto
                        {
                            Id = ir.Id,
                            RentalItemId = ir.RentalItemId,
                            ProductId = ir.ProductId,
                            ProductName = ir.Product != null ? ir.Product.Name : "Sprzęt",
                            Rating = ir.Rating,
                            Comment = ir.Comment
                        }).ToList()
                    })
                    .ToListAsync(ct);

                var result = items.Select(r => new SharedModels.RentalReviewDto
                {
                    Id = r.Id,
                    RentalId = r.RentalId,
                    CustomerName = AnonymizeName(r.CustomerName),
                    QualityScore = r.QualityScore,
                    PriceScore = r.PriceScore,
                    ServiceScore = r.ServiceScore,
                    AverageScore = (r.QualityScore + r.PriceScore + r.ServiceScore) / 3.0,
                    Comment = r.Comment,
                    CreatedAtUtc = r.CreatedAtUtc,
                    ItemReviews = r.ItemReviews
                }).ToList();

                return Results.Ok(result);
            });

            // GET /api/tenants/{tenantId}/reviews/summary — średnie i łączna liczba opinii.
            api.MapGet("/tenants/{tenantId:guid}/reviews/summary", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                Guid tenantId,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var query = db.RentalReviews.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(r => r.TenantId == tenantId && !r.IsHidden);

                var count = await query.CountAsync(ct);
                if (count == 0)
                {
                    return Results.Ok(new SharedModels.ReviewSummaryDto());
                }

                var agg = await query
                    .GroupBy(_ => 1)
                    .Select(g => new
                    {
                        AvgQ = g.Average(r => (double)r.QualityScore),
                        AvgP = g.Average(r => (double)r.PriceScore),
                        AvgS = g.Average(r => (double)r.ServiceScore)
                    })
                    .FirstAsync(ct);

                return Results.Ok(new SharedModels.ReviewSummaryDto
                {
                    Count = count,
                    AverageQuality = Math.Round(agg.AvgQ, 2),
                    AveragePrice = Math.Round(agg.AvgP, 2),
                    AverageService = Math.Round(agg.AvgS, 2),
                    AverageOverall = Math.Round((agg.AvgQ + agg.AvgP + agg.AvgS) / 3.0, 2)
                });
            });

            // GET /api/reviews — publiczna globalna lista opinii (wszystkich tenantów).
            // Klient WASM z reguły nie ma wybranej wypożyczalni, więc ta jest domyślnym źródłem.
            api.MapGet("/reviews", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                int? page,
                int? pageSize,
                CancellationToken ct) =>
            {
                var take = Math.Clamp(pageSize ?? 20, 1, 100);
                var skip = Math.Max(0, ((page ?? 1) - 1) * take);

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var items = await db.RentalReviews.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(r => !r.IsHidden)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .Skip(skip).Take(take)
                    .Select(r => new
                    {
                        r.Id,
                        r.RentalId,
                        CustomerName = r.Customer != null ? r.Customer.FullName : "Klient",
                        r.QualityScore,
                        r.PriceScore,
                        r.ServiceScore,
                        r.Comment,
                        r.CreatedAtUtc,
                        ItemReviews = r.ItemReviews.Select(ir => new SharedModels.RentalItemReviewDto
                        {
                            Id = ir.Id,
                            RentalItemId = ir.RentalItemId,
                            ProductId = ir.ProductId,
                            ProductName = ir.Product != null ? ir.Product.Name : "Sprzęt",
                            Rating = ir.Rating,
                            Comment = ir.Comment
                        }).ToList()
                    })
                    .ToListAsync(ct);

                var result = items.Select(r => new SharedModels.RentalReviewDto
                {
                    Id = r.Id,
                    RentalId = r.RentalId,
                    CustomerName = AnonymizeName(r.CustomerName),
                    QualityScore = r.QualityScore,
                    PriceScore = r.PriceScore,
                    ServiceScore = r.ServiceScore,
                    AverageScore = (r.QualityScore + r.PriceScore + r.ServiceScore) / 3.0,
                    Comment = r.Comment,
                    CreatedAtUtc = r.CreatedAtUtc,
                    ItemReviews = r.ItemReviews
                }).ToList();

                return Results.Ok(result);
            });

            // GET /api/reviews/summary — agregat globalny wszystkich opinii.
            api.MapGet("/reviews/summary", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                CancellationToken ct) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var query = db.RentalReviews.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(r => !r.IsHidden);

                var count = await query.CountAsync(ct);
                if (count == 0)
                {
                    return Results.Ok(new SharedModels.ReviewSummaryDto());
                }

                var agg = await query
                    .GroupBy(_ => 1)
                    .Select(g => new
                    {
                        AvgQ = g.Average(r => (double)r.QualityScore),
                        AvgP = g.Average(r => (double)r.PriceScore),
                        AvgS = g.Average(r => (double)r.ServiceScore)
                    })
                    .FirstAsync(ct);

                return Results.Ok(new SharedModels.ReviewSummaryDto
                {
                    Count = count,
                    AverageQuality = Math.Round(agg.AvgQ, 2),
                    AveragePrice = Math.Round(agg.AvgP, 2),
                    AverageService = Math.Round(agg.AvgS, 2),
                    AverageOverall = Math.Round((agg.AvgQ + agg.AvgP + agg.AvgS) / 3.0, 2)
                });
            });

            // POST /api/reviews/opt-out — link z maila. Token = Customer.Id chroniony przez DataProtection.
            api.MapPost("/reviews/opt-out", [AllowAnonymous] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                IDataProtectionProvider protectorProvider,
                string? token,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(token)) return Results.BadRequest(new { error = "Token required." });

                Guid customerId;
                try
                {
                    var protector = protectorProvider.CreateProtector("ReviewOptOut");
                    var raw = protector.Unprotect(token);
                    if (!Guid.TryParseExact(raw, "N", out customerId)) return Results.BadRequest(new { error = "Invalid token." });
                }
                catch
                {
                    return Results.BadRequest(new { error = "Invalid token." });
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var customer = await db.Customers.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == customerId, ct);
                if (customer is null) return Results.NotFound();

                if (!customer.ReviewEmailsOptOut)
                {
                    customer.ReviewEmailsOptOut = true;
                    await db.SaveChangesAsync(ct);
                }

                return Results.Ok(new { optedOut = true });
            });

            // Moderacja — tylko admin. Zwraca wszystkie opinie (również ukryte) dla tenanta admina.
            api.MapGet("/admin/reviews", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                ClaimsPrincipal user,
                int? page,
                int? pageSize,
                CancellationToken ct) =>
            {
                if (!user.IsAdmin()) return Results.Forbid();

                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                if (tenantId == Guid.Empty) return Results.BadRequest(new { error = "Tenant context required." });

                var take = Math.Clamp(pageSize ?? 50, 1, 200);
                var skip = Math.Max(0, ((page ?? 1) - 1) * take);

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var items = await db.RentalReviews.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(r => r.TenantId == tenantId)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .Skip(skip).Take(take)
                    .Select(r => new SharedModels.AdminReviewDto
                    {
                        Id = r.Id,
                        RentalId = r.RentalId,
                        CustomerId = r.CustomerId,
                        CustomerName = r.Customer != null ? r.Customer.FullName : "Klient",
                        CustomerEmail = r.Customer != null ? r.Customer.Email : null,
                        QualityScore = r.QualityScore,
                        PriceScore = r.PriceScore,
                        ServiceScore = r.ServiceScore,
                        AverageScore = (r.QualityScore + r.PriceScore + r.ServiceScore) / 3.0,
                        Comment = r.Comment,
                        IsHidden = r.IsHidden,
                        CreatedAtUtc = r.CreatedAtUtc
                    })
                    .ToListAsync(ct);

                return Results.Ok(items);
            });

            // PUT /api/admin/reviews/{id}/visibility — ukryj/przywróć opinię.
            api.MapPut("/admin/reviews/{id:guid}/visibility", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                ClaimsPrincipal user,
                Guid id,
                SharedModels.UpdateReviewVisibilityRequest req,
                CancellationToken ct) =>
            {
                if (!user.IsAdmin()) return Results.Forbid();

                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                if (tenantId == Guid.Empty) return Results.BadRequest(new { error = "Tenant context required." });

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var review = await db.RentalReviews.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

                if (review is null) return Results.NotFound();

                review.IsHidden = req.IsHidden;
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            });

            // DELETE /api/admin/reviews/{id} — usunięcie opinii (np. obraźliwa).
            api.MapDelete("/admin/reviews/{id:guid}", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                ClaimsPrincipal user,
                Guid id,
                CancellationToken ct) =>
            {
                if (!user.IsAdmin()) return Results.Forbid();

                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                if (tenantId == Guid.Empty) return Results.BadRequest(new { error = "Tenant context required." });

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var review = await db.RentalReviews.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

                if (review is null) return Results.NotFound();

                db.RentalReviews.Remove(review);
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            });
        }

        private static SharedModels.RentalReviewDto ToReviewDto(RentalReview r, string? customerAnonymized) => new()
        {
            Id = r.Id,
            RentalId = r.RentalId,
            CustomerName = customerAnonymized ?? (r.Customer != null ? AnonymizeName(r.Customer.FullName) : "Klient"),
            QualityScore = r.QualityScore,
            PriceScore = r.PriceScore,
            ServiceScore = r.ServiceScore,
            AverageScore = r.AverageScore,
            Comment = r.Comment,
            CreatedAtUtc = r.CreatedAtUtc
        };

        // Zwraca imię + pierwsza litera nazwiska — nie ujawniamy pełnych danych w publicznej liście.
        private static string AnonymizeName(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "Klient";
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0];
            return $"{parts[0]} {parts[^1][..1]}.";
        }

        // Customer trust scoring — RODO-friendly (3 oceny, brak komentarzy).
        // Cross-tenant agregat: każdy admin widzi globalny TrustLevel klienta, ale szczegóły
        // (kto co wystawił) są ukryte; własne recenzje widoczne tylko dla wystawiającego tenant-a.
        private static void MapCustomerTrustEndpoints(IEndpointRouteBuilder api)
        {
            // POST /api/admin/customer-reviews — wystawia ocenę klienta po zwrocie sprzętu.
            api.MapPost("/admin/customer-reviews", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ITenantProvider tenantProvider,
                ICustomerTrustCalculator trustCalc,
                ClaimsPrincipal user,
                SharedModels.CreateCustomerReviewRequest req,
                CancellationToken ct) =>
            {
                if (!user.IsAdmin()) return Results.Forbid();

                var tenantId = tenantProvider.GetCurrentTenantId() ?? Guid.Empty;
                if (tenantId == Guid.Empty) return Results.BadRequest(new { error = "Tenant context required." });

                if (req.TimelinessScore is < 0 or > 10
                    || req.ConditionScore is < 0 or > 10
                    || req.CommunicationScore is < 0 or > 10)
                {
                    return Results.BadRequest(new { error = "Scores must be between 0 and 10." });
                }

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var rental = await db.Rentals.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Id == req.RentalId, ct);
                if (rental is null || rental.TenantId != tenantId)
                {
                    return Results.NotFound(new { error = "Rental not found in current tenant." });
                }
                if (rental.Status != RentalStatus.Completed)
                {
                    return Results.BadRequest(new { error = "Customer rating only after rental is Completed." });
                }

                var alreadyExists = await db.CustomerReviews.IgnoreQueryFilters()
                    .AnyAsync(cr => cr.RentalId == req.RentalId, ct);
                if (alreadyExists)
                {
                    return Results.Conflict(new { error = "Customer review for this rental already exists." });
                }

                var review = new CustomerReview
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    CustomerId = rental.CustomerId,
                    RentalId = rental.Id,
                    TimelinessScore = req.TimelinessScore,
                    ConditionScore = req.ConditionScore,
                    CommunicationScore = req.CommunicationScore,
                    CreatedAtUtc = DateTime.UtcNow
                };

                db.CustomerReviews.Add(review);
                await db.SaveChangesAsync(ct);

                // Recalc cache fields on Customer (cross-tenant)
                await trustCalc.RecalculateAsync(rental.CustomerId, ct);

                return Results.Created($"/api/admin/customer-reviews/{review.Id}", new { id = review.Id });
            });

            // GET /api/customers/me/trust — własny status klienta (auth'd, sam siebie).
            // Zwracamy TYLKO pozytywne/neutralne poziomy (Unverified ✅ i Good 🟢) razem z
            // licznikiem ukończonych wynajmów. Statusy negatywne (Watch, Restricted) są
            // mapowane na neutralny — klient nie widzi że jest pod obserwacją; gdyby zobaczył,
            // może to wpłynąć na decyzje biznesowe (RODO ma prawo wglądu na żądanie email-owe,
            // ale w UI ukrywamy by uniknąć szantażu/odejścia klienta).
            api.MapGet("/customers/me/trust", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var customerId = user.GetCustomerId();
                if (customerId is null) return Results.Forbid();

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var customer = await db.Customers.IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == customerId, ct);
                if (customer is null) return Results.NotFound();

                // Map negative statuses to neutral for client-facing view
                var publicLevel = customer.TrustLevel == CustomerTrustLevel.Good
                    ? CustomerTrustLevel.Good
                    : CustomerTrustLevel.Unverified;
                var (label, emoji) = TrustDescription(publicLevel);

                return Results.Ok(new SharedModels.CustomerTrustSummaryDto
                {
                    CustomerId = customer.Id,
                    TrustLevel = (int)publicLevel,
                    TrustLabel = label,
                    TrustEmoji = emoji,
                    CompletedRentals = customer.TrustCompletedRentalsCount,
                    AverageScore = customer.TrustAverageScore,
                    IncidentCount = 0,           // ukrywamy przed klientem
                    CalculatedAtUtc = customer.TrustLevelCalculatedAtUtc,
                    IsManualOverride = false     // ukrywamy przed klientem
                });
            });

            // GET /api/admin/customers/{id}/trust-summary — agregat zaufania (cross-tenant).
            api.MapGet("/admin/customers/{id:guid}/trust-summary", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ClaimsPrincipal user,
                Guid id,
                CancellationToken ct) =>
            {
                if (!user.IsAdmin()) return Results.Forbid();

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var (tenantId, isSuperAdmin) = ResolveTenantContext(user);
                if (!isSuperAdmin)
                {
                    if (tenantId == Guid.Empty) return Results.Forbid();
                    var hasRelationship = await db.Customers.IgnoreQueryFilters()
                        .AnyAsync(c => c.Id == id &&
                            (c.TenantId == tenantId || db.Rentals.IgnoreQueryFilters()
                                .Any(r => r.CustomerId == c.Id && r.TenantId == tenantId)), ct);
                    if (!hasRelationship) return Results.NotFound();
                }

                var customer = await db.Customers.IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == id, ct);
                if (customer is null) return Results.NotFound();

                var (label, emoji) = TrustDescription(customer.TrustLevel);
                return Results.Ok(new SharedModels.CustomerTrustSummaryDto
                {
                    CustomerId = customer.Id,
                    TrustLevel = (int)customer.TrustLevel,
                    TrustLabel = label,
                    TrustEmoji = emoji,
                    CompletedRentals = customer.TrustCompletedRentalsCount,
                    AverageScore = customer.TrustAverageScore,
                    IncidentCount = customer.TrustIncidentCount,
                    CalculatedAtUtc = customer.TrustLevelCalculatedAtUtc,
                    IsManualOverride = customer.TrustLevelManualOverride.HasValue
                });
            });

            // PATCH /api/admin/customers/{id}/trust-override — manual block / override.
            api.MapPatch("/admin/customers/{id:guid}/trust-override", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                ICustomerTrustCalculator trustCalc,
                ClaimsPrincipal user,
                Guid id,
                SharedModels.UpdateCustomerTrustOverrideRequest req,
                CancellationToken ct) =>
            {
                // Override jest globalny, więc tenantowy Owner/Employee nie może zmieniać
                // statusu używanego przez inne wypożyczalnie.
                if (!user.IsInRole(SportRental.Shared.Identity.RoleNames.SuperAdmin))
                    return Results.Forbid();

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var customer = await db.Customers.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == id, ct);
                if (customer is null) return Results.NotFound();

                if (req.TrustLevel is null)
                {
                    customer.TrustLevelManualOverride = null;
                    customer.TrustLevelManualReason = null;
                }
                else
                {
                    if (req.TrustLevel is < 0 or > 3)
                        return Results.BadRequest(new { error = "TrustLevel must be 0-3 (Unverified..Restricted)." });
                    customer.TrustLevelManualOverride = (CustomerTrustLevel)req.TrustLevel.Value;
                    customer.TrustLevelManualReason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
                }
                await db.SaveChangesAsync(ct);

                await trustCalc.RecalculateAsync(customer.Id, ct);
                return Results.NoContent();
            });
        }

        private static (string label, string emoji) TrustDescription(CustomerTrustLevel level) => level switch
        {
            CustomerTrustLevel.Good       => ("Bez szkód", "🟢"),
            CustomerTrustLevel.Watch      => ("Wymaga uwagi", "🟡"),
            CustomerTrustLevel.Restricted => ("Konto ograniczone", "🔴"),
            _                             => ("Zweryfikowany", "✅")
        };

        private static SharedModels.RentalTermsSummary BuildRentalTermsSummary(CompanyInfo? company)
        {
            var customTerms = company?.RegulationsText?.Trim();
            if (string.IsNullOrWhiteSpace(customTerms))
            {
                var fallbackContent = DefaultRentalRegulations.Content;
                return new SharedModels.RentalTermsSummary
                {
                    Title = "Standardowy regulamin wypożyczalni RentSpot",
                    Version = DefaultRentalRegulations.Version,
                    ContentHash = Convert.ToHexString(
                        SHA256.HashData(Encoding.UTF8.GetBytes(fallbackContent))).ToLowerInvariant(),
                    Content = fallbackContent,
                    UsesPlatformDefault = true
                };
            }

            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(customTerms))).ToLowerInvariant();
            return new SharedModels.RentalTermsSummary
            {
                Title = string.IsNullOrWhiteSpace(company?.Name)
                    ? "Regulamin wypożyczalni"
                    : $"Regulamin wypożyczalni {company.Name}",
                Version = $"tenant-{hash[..16]}",
                ContentHash = hash,
                Content = customTerms,
                UsesPlatformDefault = false
            };
        }

        private static async Task<List<SharedModels.CheckoutRentalGroupRequest>> NormalizeCheckoutRentalGroupsAsync(
            SharedModels.CreateCheckoutSessionRequest request,
            ApplicationDbContext db,
            CancellationToken ct)
        {
            List<SharedModels.CheckoutRentalGroupRequest> groups;
            if (request.RentalGroups is { Count: > 0 })
            {
                groups = request.RentalGroups.ToList();
            }
            else
            {
                var legacyItems = request.Items ?? new List<SharedModels.CheckoutItem>();
                if (legacyItems.Count == 0)
                    throw new InvalidOperationException("Koszyk jest pusty.");

                var legacyProductIds = legacyItems.Select(item => item.ProductId).Distinct().ToList();
                var productTenants = await db.Products.IgnoreQueryFilters()
                    .Where(product => legacyProductIds.Contains(product.Id))
                    .Select(product => new { product.Id, product.TenantId })
                    .ToDictionaryAsync(product => product.Id, product => product.TenantId, ct);
                if (productTenants.Count != legacyProductIds.Count)
                    throw new InvalidOperationException("Co najmniej jeden produkt nie istnieje.");

                groups = legacyItems
                    .GroupBy(item => productTenants[item.ProductId])
                    .Select(group => new SharedModels.CheckoutRentalGroupRequest(
                        group.Key,
                        request.StartDateUtc,
                        request.EndDateUtc,
                        group.ToList(),
                        request.RentalType,
                        request.HoursRented))
                    .ToList();
            }

            if (groups.Count == 0)
                throw new InvalidOperationException("Koszyk jest pusty.");
            if (groups.Count > 10)
                throw new InvalidOperationException("Jedno zamówienie może obejmować maksymalnie 10 wypożyczalni.");
            if (groups.Any(group => group.TenantId == Guid.Empty))
                throw new InvalidOperationException("Każda rezerwacja musi wskazywać wypożyczalnię.");
            if (groups.GroupBy(group => group.TenantId).Any(group => group.Count() > 1))
                throw new InvalidOperationException("Jedna wypożyczalnia może wystąpić tylko raz w zamówieniu.");
            if (groups.Any(group => group.Items is null || group.Items.Count == 0))
                throw new InvalidOperationException("Każda rezerwacja musi zawierać sprzęt.");
            if (groups.Any(group => group.StartDateUtc >= group.EndDateUtc))
                throw new InvalidOperationException("Data zakończenia musi być późniejsza od rozpoczęcia.");
            if (groups.Any(group => !PolishRentalTime.IsStartSafelyInFuture(group.StartDateUtc, DateTime.UtcNow)))
                throw new InvalidOperationException("Data rozpoczęcia musi być co najmniej 2 minuty w przyszłości.");

            var allItems = groups.SelectMany(group => group.Items).ToList();
            if (allItems.Count > 50)
                throw new InvalidOperationException("Jedno zamówienie może zawierać maksymalnie 50 produktów.");
            if (allItems.Any(item => item.Quantity <= 0) ||
                allItems.GroupBy(item => item.ProductId).Any(group => group.Count() > 1))
            {
                throw new InvalidOperationException("Pozycje koszyka są nieprawidłowe.");
            }

            var productIds = allItems.Select(item => item.ProductId).ToList();
            var actualProductTenants = await db.Products.IgnoreQueryFilters()
                .Where(product => productIds.Contains(product.Id))
                .Select(product => new { product.Id, product.TenantId })
                .ToDictionaryAsync(product => product.Id, product => product.TenantId, ct);
            if (actualProductTenants.Count != productIds.Count)
                throw new InvalidOperationException("Co najmniej jeden produkt nie istnieje.");

            foreach (var group in groups)
            {
                if (group.Items.Any(item => actualProductTenants[item.ProductId] != group.TenantId))
                    throw new InvalidOperationException("Produkt został przypisany do niewłaściwej wypożyczalni.");
            }

            return groups.OrderBy(group => group.TenantId).ToList();
        }

        // Checkout endpoints for Stripe redirect flow
        private static void MapCheckoutEndpoints(IEndpointRouteBuilder api)
        {
            var checkout = api.MapGroup("/checkout");

            // POST /api/checkout/create-session
            checkout.MapPost("/create-session", [Authorize(AuthenticationSchemes = ApiAuthSchemes)] async (
                IDbContextFactory<ApplicationDbContext> dbFactory,
                IConfiguration configuration,
                IWebHostEnvironment environment,
                Microsoft.Extensions.Options.IOptions<Payments.StripeOptions> stripeOptions,
                SportRental.Admin.Services.IBusinessHoursService businessHours,
                ClaimsPrincipal user,
                ILoggerFactory loggerFactory,
                SharedModels.CreateCheckoutSessionRequest request,
                CancellationToken ct) =>
            {
                if (!string.Equals(
                        request.AcceptedTermsVersion,
                        LegalDocumentVersions.Terms,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        request.AcknowledgedPrivacyVersion,
                        LegalDocumentVersions.Privacy,
                        StringComparison.Ordinal))
                {
                    return Results.BadRequest(new
                    {
                        error = "Przed płatnością zaakceptuj aktualny regulamin i potwierdź zapoznanie się z polityką prywatności."
                    });
                }

                if (request.RentalGroups is not { Count: > 0 })
                {
                    return Results.BadRequest(new
                    {
                        error = "Odśwież aplikację i zaakceptuj regulamin każdej wypożyczalni przed płatnością."
                    });
                }

                var customerId = user.GetCustomerId();
                if (!customerId.HasValue)
                    return Results.Forbid();
                if (request.CustomerId.HasValue && request.CustomerId.Value != customerId.Value)
                    return Results.NotFound();

                await using var db = await dbFactory.CreateDbContextAsync(ct);

                try
                {
                    var rentalGroups = await NormalizeCheckoutRentalGroupsAsync(request, db, ct);
                    var checkoutItems = rentalGroups.SelectMany(group => group.Items).ToList();
                    if (checkoutItems.Any(item => !item.HoldId.HasValue))
                        return Results.Conflict(new { error = "Rezerwacja produktów wygasła. Odśwież koszyk." });

                    // Każda wypożyczalnia ma własny termin i własne godziny pracy.
                    // Weryfikujemy wszystkie grupy przed przekierowaniem do Stripe.
                    foreach (var group in rentalGroups)
                    {
                        var window = await businessHours.ValidateRentalWindowAsync(
                            group.TenantId,
                            group.StartDateUtc,
                            group.EndDateUtc,
                            ct);
                        if (!window.IsValid)
                        {
                            return Results.BadRequest(new
                            {
                                error = window.Reason ?? "Wypożyczalnia jest zamknięta w wybranym terminie.",
                                tenantId = group.TenantId
                            });
                        }
                    }

                    var nowUtc = DateTime.UtcNow;
                    if (!user.IsInRole("GuestCustomer") &&
                        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var applicationUserId))
                    {
                        var applicationUser = await db.Users
                            .FirstOrDefaultAsync(candidate => candidate.Id == applicationUserId, ct);
                        if (applicationUser is not null &&
                            (!string.Equals(applicationUser.AcceptedTermsVersion, LegalDocumentVersions.Terms, StringComparison.Ordinal) ||
                             !string.Equals(applicationUser.AcknowledgedPrivacyVersion, LegalDocumentVersions.Privacy, StringComparison.Ordinal) ||
                             applicationUser.LegalAcceptedAtUtc is null))
                        {
                            applicationUser.AcceptedTermsVersion = LegalDocumentVersions.Terms;
                            applicationUser.AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy;
                            applicationUser.LegalAcceptedAtUtc = nowUtc;
                        }
                    }

                    var holdIds = checkoutItems.Select(i => i.HoldId!.Value).Distinct().ToList();
                    var holds = await db.ReservationHolds.IgnoreQueryFilters()
                        .Where(h => holdIds.Contains(h.Id) && h.ExpiresAtUtc > nowUtc)
                        .ToListAsync(ct);
                    if (holds.Count != checkoutItems.Count)
                        return Results.Conflict(new { error = "Co najmniej jedna rezerwacja produktu wygasła." });

                    foreach (var group in rentalGroups)
                    {
                        foreach (var item in group.Items)
                        {
                            var hold = holds.SingleOrDefault(h => h.Id == item.HoldId);
                            if (hold is null || hold.TenantId != group.TenantId ||
                                hold.ProductId != item.ProductId || hold.Quantity != item.Quantity ||
                                Math.Abs((hold.StartDateUtc - group.StartDateUtc).TotalSeconds) > 1 ||
                                Math.Abs((hold.EndDateUtc - group.EndDateUtc).TotalSeconds) > 1)
                            {
                                return Results.Conflict(new { error = "Koszyk zmienił się po utworzeniu rezerwacji. Odśwież go." });
                            }

                            var ownedByCustomer = hold.CustomerId == customerId.Value;
                            var ownedBySession = SessionIdEquals(hold.SessionId, request.HoldSessionId);
                            if (!ownedByCustomer && !ownedBySession)
                                return Results.NotFound();
                        }
                    }

                    var computation = await Payments.PaymentCalculator.ComputeAsync(
                        Guid.Empty,
                        new SharedModels.PaymentQuoteRequest
                        {
                            RentalGroups = rentalGroups.Select(group => new SharedModels.RentalGroupQuoteRequest
                            {
                                TenantId = group.TenantId,
                                StartDateUtc = group.StartDateUtc,
                                EndDateUtc = group.EndDateUtc,
                                RentalType = group.RentalType,
                                HoursRented = group.HoursRented,
                                Items = group.Items.Select(item => new SharedModels.CreateRentalItem
                                {
                                    ProductId = item.ProductId,
                                    Quantity = item.Quantity
                                }).ToList()
                            }).ToList()
                        },
                        db,
                        ct);

                    var checkoutTenantIds = computation.Tenants.Select(group => group.TenantId).Distinct().ToList();
                    var checkoutTenantNames = await db.Tenants.IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(tenant => checkoutTenantIds.Contains(tenant.Id))
                        .ToDictionaryAsync(tenant => tenant.Id, tenant => tenant.Name, ct);
                    var checkoutCompanyInfos = await db.CompanyInfos.IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(info => checkoutTenantIds.Contains(info.TenantId))
                        .ToDictionaryAsync(info => info.TenantId, ct);
                    var rentalTerms = checkoutTenantIds.ToDictionary(
                        id => id,
                        id => BuildRentalTermsSummary(checkoutCompanyInfos.GetValueOrDefault(id)));

                    // Checkout wymaga hasha dokumentu wyświetlonego przy wycenie
                    // osobno dla każdej wypożyczalni.
                    foreach (var group in rentalGroups)
                    {
                        var expectedHash = rentalTerms[group.TenantId].ContentHash;
                        if (!string.Equals(
                                group.AcceptedRegulationsHash,
                                expectedHash,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return Results.BadRequest(new
                            {
                                error = "Regulamin jednej z wypożyczalni zmienił się. Odśwież podsumowanie i zaakceptuj aktualną wersję.",
                                tenantId = group.TenantId
                            });
                        }
                    }

                    var customer = await db.Customers
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Id == customerId.Value, ct);

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

                    // Redirecty płatności są linkami bezpieczeństwa: produkcja nie może
                    // ufać Request.Host. Publiczny origin pochodzi wyłącznie z konfiguracji.
                    if (!ClientAppUrlResolver.TryResolveSecurityBaseUrl(
                            configuration,
                            environment,
                            out var clientBaseUrl))
                    {
                        loggerFactory.CreateLogger("Checkout")
                            .LogError(
                                "Nie utworzono checkoutu: ustaw poprawny HTTPS ClientApp:PublicBaseUrl lub Admin:PublicBaseUrl");
                        return Results.Json(
                            new { error = "Płatności online są chwilowo niedostępne z powodu konfiguracji adresu aplikacji." },
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    
                    var successUrl = stripe.SuccessUrl ?? configuration["Stripe:SuccessUrl"] ?? $"{clientBaseUrl}/checkout/success";
                    var cancelUrl = stripe.CancelUrl ?? configuration["Stripe:CancelUrl"] ?? $"{clientBaseUrl}/checkout/cancel";

                    // Ten sam zestaw holdów może mieć dokładnie jedną płatność. Klucz jest
                    // stabilny także przy równoległym dwukliku/retry po utracie odpowiedzi.
                    var idempotencyKey = Payments.CheckoutIdempotencyKey.Create(customerId.Value, holdIds);
                    var depositAmount = computation.DepositAmount <= 0 ? computation.TotalAmount : computation.DepositAmount;
                    var depositUnitAmount = Math.Max(1, (long)Math.Round(depositAmount * 100, MidpointRounding.AwayFromZero));

                    var checkoutPayload = new Payments.CheckoutRentalPayload
                    {
                        SchemaVersion = 2,
                        Customer = new Payments.CheckoutCustomerSnapshot
                        {
                            CustomerId = customer.Id,
                            FullName = customer.FullName,
                            Email = customer.Email,
                            PhoneNumber = customer.PhoneNumber,
                            Address = customer.Address,
                            DocumentNumber = customer.DocumentNumber
                        },
                        // Pola globalne są zachowane dla odczytu starszych payloadów;
                        // docelowym źródłem są terminy wewnątrz każdej grupy tenant-a.
                        StartDateUtc = rentalGroups[0].StartDateUtc,
                        EndDateUtc = rentalGroups[0].EndDateUtc,
                        Tenants = computation.Tenants
                            .Select((group, index) =>
                            {
                                var terms = rentalTerms[group.TenantId];
                                return new Payments.CheckoutTenantPayload
                                {
                                    Sequence = index + 1,
                                    TenantId = group.TenantId,
                                    TenantName = checkoutTenantNames.GetValueOrDefault(group.TenantId) ?? "Wypożyczalnia",
                                    StartDateUtc = group.StartDateUtc,
                                    EndDateUtc = group.EndDateUtc,
                                    RentalType = group.RentalType,
                                    HoursRented = group.HoursRented,
                                    Items = group.Items.Select(item => new Payments.CheckoutRentalItemPayload
                                    {
                                        ProductId = item.ProductId,
                                        Quantity = item.Quantity,
                                        PricePerDay = item.PricePerDay,
                                        PricePerHour = item.PricePerHour,
                                        Subtotal = item.Subtotal
                                    }).ToList(),
                                    TotalAmount = group.TotalAmount,
                                    DepositAmount = group.DepositAmount,
                                    RegulationsTextSnapshot = terms.Content,
                                    RegulationsHash = terms.ContentHash,
                                    RegulationsVersion = terms.Version,
                                    RegulationsSource = terms.UsesPlatformDefault ? "PlatformDefault" : "TenantCustom"
                                };
                            })
                            .ToList(),
                        Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                        IdempotencyKey = idempotencyKey,
                        TotalAmount = computation.TotalAmount,
                        DepositAmount = depositAmount,
                        RentalType = rentalGroups[0].RentalType,
                        HoursRented = rentalGroups[0].RentalType == SharedModels.RentalTypeDto.Hourly
                            ? rentalGroups[0].HoursRented
                            : null,
                        HoldIds = holdIds,
                        AcceptedTermsVersion = LegalDocumentVersions.Terms,
                        AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy
                    };

                    if (checkoutPayload.Tenants.Count == 0)
                    {
                        return Results.BadRequest(new { error = "Brak pozycji do finalizacji." });
                    }

                    var tenantIds = checkoutPayload.Tenants.Select(t => t.TenantId).Distinct().ToList();
                    
                    // Zapisz payload w bazie danych zamiast w metadata Stripe (limit 500 znaków)
                    var payloadJson = System.Text.Json.JsonSerializer.Serialize(checkoutPayload);
                    var checkoutSession = await db.CheckoutSessions
                        .FirstOrDefaultAsync(cs => cs.IdempotencyKey == idempotencyKey, ct);
                    if (checkoutSession is not null)
                    {
                        if (checkoutSession.IsProcessed)
                            return Results.Conflict(new { error = "Ta płatność została już rozliczona." });
                        if (!string.Equals(checkoutSession.PayloadJson, payloadJson, StringComparison.Ordinal))
                            return Results.Conflict(new { error = "Dane koszyka zmieniły się. Odśwież rezerwację produktów." });
                    }
                    else
                    {
                        checkoutSession = new Infrastructure.Domain.CheckoutSession
                        {
                            Id = Guid.NewGuid(),
                            IdempotencyKey = idempotencyKey,
                            PayloadJson = payloadJson,
                            CreatedAtUtc = DateTime.UtcNow,
                            AcceptedTermsVersion = LegalDocumentVersions.Terms,
                            AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy,
                            LegalAcceptedAtUtc = DateTime.UtcNow,
                            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(40)
                        };
                        db.CheckoutSessions.Add(checkoutSession);
                        try
                        {
                            await db.SaveChangesAsync(ct);
                        }
                        catch (DbUpdateException)
                        {
                            // Unikalny IdempotencyKey rozstrzyga równoległe żądania przed
                            // kontaktem ze Stripe. Klient może bezpiecznie ponowić próbę.
                            return Results.Conflict(new { error = "Płatność dla tego koszyka jest już inicjowana. Spróbuj ponownie za chwilę." });
                        }
                    }

                    var metadata = new Dictionary<string, string>
                    {
                        ["tenant_ids"] = string.Join(",", tenantIds),
                        ["customer_id"] = customer.Id.ToString(),
                        ["rental_start"] = rentalGroups.Min(group => group.StartDateUtc).ToString("O"),
                        ["rental_end"] = rentalGroups.Max(group => group.EndDateUtc).ToString("O"),
                        ["items_count"] = checkoutItems.Count.ToString(),
                        ["rentals_count"] = rentalGroups.Count.ToString(),
                        ["idempotency_key"] = idempotencyKey,
                        ["checkout_session_id"] = checkoutSession.Id.ToString()
                    };

                    var customerEmail = customer.Email;
                    if (string.IsNullOrWhiteSpace(customerEmail))
                        return Results.BadRequest(new { error = "Konto klienta nie ma adresu e-mail." });

                    var sessionService = new Stripe.Checkout.SessionService();
                    if (!string.IsNullOrWhiteSpace(checkoutSession.StripeSessionId))
                    {
                        var existingStripeSession = await sessionService.GetAsync(
                            checkoutSession.StripeSessionId,
                            cancellationToken: ct);
                        if (string.Equals(existingStripeSession.Status, "open", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(existingStripeSession.Url))
                        {
                            return Results.Ok(new SharedModels.CheckoutSessionResponse(
                                existingStripeSession.Id,
                                existingStripeSession.Url,
                                existingStripeSession.ExpiresAt,
                                checkoutSession.ExpiresAtUtc));
                        }

                        return Results.Conflict(new
                        {
                            error = string.Equals(existingStripeSession.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                                ? "Płatność została przyjęta i jest rozliczana."
                                : "Poprzednia sesja płatności wygasła. Odśwież koszyk."
                        });
                    }

                    var stripeLineItems = checkoutPayload.Tenants
                        .Select(tenant => new Stripe.Checkout.SessionLineItemOptions
                        {
                            PriceData = new Stripe.Checkout.SessionLineItemPriceDataOptions
                            {
                                Currency = "pln",
                                UnitAmount = Math.Max(
                                    1,
                                    (long)Math.Round(
                                        tenant.DepositAmount * 100m,
                                        MidpointRounding.AwayFromZero)),
                                ProductData = new Stripe.Checkout.SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = $"Zwrotny depozyt — {tenant.TenantName}",
                                    Description = tenant.RentalType == SharedModels.RentalTypeDto.Hourly
                                        ? $"Rezerwacja godzinowa: {tenant.StartDateUtc:g}–{tenant.EndDateUtc:g}"
                                        : $"Rezerwacja: {tenant.StartDateUtc:d}–{tenant.EndDateUtc:d}"
                                }
                            },
                            Quantity = 1
                        })
                        .ToList();

                    if (stripeLineItems.Sum(item => item.PriceData?.UnitAmount ?? 0) != depositUnitAmount)
                    {
                        return Results.BadRequest(new
                        {
                            error = "Nie udało się uzgodnić kwoty depozytu dla wypożyczalni. Odśwież wycenę."
                        });
                    }

                    var sessionOptions = new Stripe.Checkout.SessionCreateOptions
                    {
                        SuccessUrl = successUrl + (successUrl.Contains('?') ? "&" : "?") + "session_id={CHECKOUT_SESSION_ID}",
                        CancelUrl = cancelUrl,
                        Mode = "payment",
                        CustomerEmail = customerEmail,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(35),
                        PaymentIntentData = new Stripe.Checkout.SessionPaymentIntentDataOptions
                        {
                            Metadata = metadata,
                            CaptureMethod = "automatic"
                        },
                        LineItems = stripeLineItems,
                        Metadata = metadata
                    };

                    var session = await sessionService.CreateAsync(
                        sessionOptions,
                        new Stripe.RequestOptions { IdempotencyKey = idempotencyKey },
                        ct);
                    if (string.IsNullOrWhiteSpace(session.Url))
                        return Results.BadRequest(new { error = "Stripe nie zwrócił adresu płatności." });

                    checkoutSession.StripeSessionId = session.Id;
                    checkoutSession.ExpiresAtUtc = session.ExpiresAt.AddMinutes(5);
                    foreach (var hold in holds)
                        hold.ExpiresAtUtc = checkoutSession.ExpiresAtUtc;
                    await db.SaveChangesAsync(ct);

                    return Results.Ok(new SharedModels.CheckoutSessionResponse(
                        session.Id,
                        session.Url,
                        session.ExpiresAt,
                        checkoutSession.ExpiresAtUtc));
                }
                catch (InvalidOperationException ex)
                {
                    loggerFactory.CreateLogger("Checkout")
                        .LogWarning(ex, "Odrzucono utworzenie sesji checkout");
                    return Results.BadRequest(new { error = ex.Message });
                }
                catch (Stripe.StripeException ex)
                {
                    loggerFactory.CreateLogger("Checkout")
                        .LogError(ex, "Stripe nie utworzył sesji checkout");
                    return Results.Json(
                        new { error = "Operator płatności jest chwilowo niedostępny. Spróbuj ponownie." },
                        statusCode: StatusCodes.Status502BadGateway);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger("Checkout")
                        .LogError(ex, "Nieoczekiwany błąd tworzenia sesji checkout");
                    return Results.Json(
                        new { error = "Nie udało się rozpocząć płatności. Spróbuj ponownie." },
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            });

            // POST /api/checkout/finalize-session/{sessionId}
            // FALLBACK ścieżka — gdy klient wraca z Stripe redirectu. Logika identyczna jak
            // w webhook handlerze (`/api/payments/webhook`). Pierwsza ścieżka która zdąży
            // (zazwyczaj webhook bo Stripe wysyła go natychmiast) tworzy rental; druga widzi
            // existing przez idempotency key i wraca success bez duplikacji.
            checkout.MapPost("/finalize-session/{sessionId}", [AllowAnonymous] async (
                string sessionId,
                Payments.CheckoutFinalizationService finalizer,
                CancellationToken ct) =>
            {
                var result = await finalizer.FinalizeAsync(sessionId, ct);
                return result.Success
                    ? Results.Ok(result)
                    : Results.Json(result, statusCode: StatusCodes.Status409Conflict);
            });

            // POST /api/payments/webhook — primary source of truth. Stripe wysyła event po
            // pomyślnej płatności (checkout.session.completed). Weryfikujemy podpis HMAC żeby
            // tylko Stripe mógł trigger-ować finalizację. Bez tego endpointu klient zamykający
            // kartę przed redirectem powodował: payment OK, ale rental nigdy nie był utworzony.
            api.MapPost("/payments/webhook", [AllowAnonymous] async (
                HttpRequest request,
                Payments.CheckoutFinalizationService finalizer,
                Microsoft.Extensions.Options.IOptions<Payments.StripeOptions> stripeOptions,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var stripe = stripeOptions.Value;
                var logger = loggerFactory.CreateLogger("StripeWebhook");
                if (string.IsNullOrWhiteSpace(stripe.WebhookSecret))
                {
                    logger.LogWarning("Stripe webhook hit ale WebhookSecret nieskonfigurowany");
                    return Results.BadRequest(new { error = "Stripe webhook not configured" });
                }

                using var reader = new StreamReader(request.Body);
                var json = await reader.ReadToEndAsync(ct);
                var sigHeader = request.Headers["Stripe-Signature"].ToString();

                Stripe.Event evt;
                try
                {
                    evt = Stripe.EventUtility.ConstructEvent(json, sigHeader, stripe.WebhookSecret);
                }
                catch (Stripe.StripeException ex)
                {
                    logger.LogWarning(ex, "Stripe webhook signature verification failed");
                    return Results.BadRequest(new { error = "Signature verification failed" });
                }

                if (evt.Type == "checkout.session.completed" || evt.Type == Stripe.EventTypes.CheckoutSessionCompleted)
                {
                    var session = evt.Data.Object as Stripe.Checkout.Session;
                    if (session != null)
                    {
                        logger.LogInformation("Stripe webhook: checkout.session.completed {SessionId}", session.Id);
                        var result = await finalizer.FinalizeAsync(session.Id, ct);
                        if (!result.Success && !result.Refunded)
                        {
                            logger.LogWarning("Webhook finalize NIE udało się dla {SessionId}: {Reason}", session.Id, result.Message);
                            return Results.StatusCode(StatusCodes.Status500InternalServerError);
                        }
                        if (result.Refunded)
                        {
                            logger.LogWarning(
                                "Webhook finalize zakończył się automatycznym zwrotem dla {SessionId}: {Reason}",
                                session.Id,
                                result.Message);
                        }
                    }
                }
                else
                {
                    logger.LogDebug("Stripe webhook: ignoruję event type {Type}", evt.Type);
                }

                return Results.Ok();
            });

        }
        
#pragma warning disable CS0618 // The whole method is the intentionally retained legacy flow.
        [Obsolete("Legacy SMS reply confirmation endpoints. Use /confirm/{token} link confirmation instead.")]
        private static void MapLegacySmsConfirmationEndpoints(IEndpointRouteBuilder api)
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
            
            // Uniwersalny endpoint POST — obsługuje zarówno SerwerSMS.pl jak i SMSAPI.pl
            // SerwerSMS: phone/numer, text/wiadomosc/message, id/message_id
            // SMSAPI:    sms_from, sms_text, sms_to, sms_date, username
            // URL callback w panelu SMSAPI: https://sradmin.azurewebsites.net/api/sms/incoming
            sms.MapPost("/incoming", [AllowAnonymous] async (
                ISmsConfirmationService confirmationService,
                ILoggerFactory loggerFactory,
                HttpRequest request,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("SmsWebhook");
                var form = await request.ReadFormAsync(ct);
                
                // SMSAPI.pl format: sms_from, sms_text
                // SerwerSMS.pl format: phone/numer, text/wiadomosc/message
                var numer = form["sms_from"].FirstOrDefault() 
                    ?? form["phone"].FirstOrDefault() 
                    ?? form["numer"].FirstOrDefault();
                var wiadomosc = form["sms_text"].FirstOrDefault() 
                    ?? form["text"].FirstOrDefault() 
                    ?? form["wiadomosc"].FirstOrDefault() 
                    ?? form["message"].FirstOrDefault();
                var id = form["id"].FirstOrDefault() ?? form["message_id"].FirstOrDefault();
                var smsTo = form["sms_to"].FirstOrDefault();
                var smsDate = form["sms_date"].FirstOrDefault();
                var username = form["username"].FirstOrDefault();
                
                logger.LogInformation(
                    "Incoming SMS POST webhook: from={Numer}, text={Wiadomosc}, id={Id}, to={SmsTo}, date={SmsDate}, user={Username}", 
                    numer, wiadomosc, id, smsTo, smsDate, username);
                
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
#pragma warning restore CS0618
    }

    // DTOs for auth endpoints
    public record RegisterRequest(
        string Email,
        string Password,
        string? FullName,
        string? PhoneNumber,
        string? DocumentNumber,
        string? AcceptedTermsVersion = null,
        string? AcknowledgedPrivacyVersion = null);
    public record LoginRequest(string Email, string Password);
    public record ResendEmailConfirmationRequest(string Email);
}
