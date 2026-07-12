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

        public async Task<byte[]> GenerateRentalContractAsync(
            Rental rental,
            IEnumerable<RentalItem> items,
            Customer customer,
            IEnumerable<Product> products,
            CompanyInfo? companyInfo = null,
            CancellationToken ct = default)
        {
            var model = await CreateDocumentModelAsync(
                rental,
                items,
                customer,
                products,
                companyInfo,
                ContractTemplateDefaults.StandardTerms,
                ct);

            return GenerateDocument(model);
        }

        public async Task<byte[]> GenerateRentalContractAsync(
            string templateContent,
            Rental rental,
            IEnumerable<RentalItem> items,
            Customer customer,
            IEnumerable<Product> products,
            CompanyInfo? companyInfo = null,
            CancellationToken ct = default)
        {
            var itemList = items.ToList();
            var productList = products.ToList();
            var productMap = productList.ToDictionary(product => product.Id, product => product);
            var filledTemplate = FillTemplate(
                templateContent,
                rental,
                itemList,
                customer,
                productMap,
                companyInfo);
            var termsContent = ResolveTermsContent(templateContent, filledTemplate);

            var model = await CreateDocumentModelAsync(
                rental,
                itemList,
                customer,
                productList,
                companyInfo,
                termsContent,
                ct);

            return GenerateDocument(model);
        }

        private byte[] GenerateDocument(RentalContractDocumentModel model)
        {
            try
            {
                return RentalContractDocument.Generate(model);
            }
            catch (Exception ex) when (model.Branding.LogoBytes is { Length: > 0 })
            {
                _logger.LogWarning(
                    ex,
                    "Nie udało się osadzić logo w umowie {RentalReference}; ponawiam render bez obrazu",
                    model.Reference);
                var safeModel = model with
                {
                    Branding = model.Branding with { LogoBytes = null }
                };
                return RentalContractDocument.Generate(safeModel);
            }
        }

        private async Task<RentalContractDocumentModel> CreateDocumentModelAsync(
            Rental rental,
            IEnumerable<RentalItem> items,
            Customer customer,
            IEnumerable<Product> products,
            CompanyInfo? companyInfo,
            string termsContent,
            CancellationToken ct)
        {
            var itemList = items.ToList();
            var productMap = products.ToDictionary(product => product.Id, product => product);
            var startsAt = PolishTimeZone.FromUtc(rental.StartDateUtc);
            var endsAt = PolishTimeZone.FromUtc(rental.EndDateUtc);
            var issuedAt = PolishTimeZone.FromUtc(DateTime.UtcNow);
            var billedUnits = GetBilledUnits(rental, startsAt, endsAt);
            var durationText = FormatRentalDuration(rental.RentalType, billedUnits);
            var usesVariableDailyPrice = rental.RentalType == RentalType.Daily
                && itemList.Any(item =>
                    Math.Abs(ResolveDisplayedUnitPrice(rental, item, billedUnits) - item.PricePerDay) >= 0.005m);
            var priceUnitLabel = rental.RentalType == RentalType.Hourly
                ? "CENA / GODZ."
                : usesVariableDailyPrice ? "ŚR. CENA / DZIEŃ" : "CENA / DZIEŃ";

            var companyName = SanitizePdfText(
                string.IsNullOrWhiteSpace(companyInfo?.Name) ? "RentSpot Partner" : companyInfo.Name);
            var companyDetails = companyInfo == null
                ? []
                : BuildCompanyDetails(companyInfo);
            var customerDetails = BuildCustomerDetails(customer);
            var logo = await LoadLogoAsync(companyInfo?.Tenant, ct);
            var branding = ResolveBranding(companyInfo?.Tenant, logo);

            var contractItems = itemList
                .Select(item =>
                {
                    var product = productMap.GetValueOrDefault(item.ProductId);
                    return new RentalContractLine(
                        SanitizePdfText(product?.Name ?? $"Sprzęt {item.ProductId.ToString()[..8]}"),
                        item.Quantity,
                        ResolveDisplayedUnitPrice(rental, item, billedUnits),
                        item.Subtotal);
                })
                .ToList();
            var regulations = string.IsNullOrWhiteSpace(rental.RegulationsTextSnapshot)
                ? null
                : new RentalContractRegulations(
                    SanitizePdfText(rental.RegulationsTextSnapshot),
                    SanitizePdfText(rental.RegulationsVersion),
                    SanitizePdfText(rental.RegulationsHash),
                    SanitizePdfText(rental.RegulationsSource));

            return new RentalContractDocumentModel(
                rental.Id.ToString()[..8].ToUpperInvariant(),
                issuedAt,
                startsAt,
                endsAt,
                durationText,
                priceUnitLabel,
                companyName,
                companyDetails,
                SanitizePdfText(customer.FullName),
                customerDetails,
                contractItems,
                rental.TotalAmount,
                rental.DepositAmount,
                ParseContractTerms(termsContent),
                SanitizePdfText(rental.Notes),
                regulations,
                branding);
        }

        private static int GetBilledUnits(Rental rental, DateTime startsAtLocal, DateTime endsAtLocal)
        {
            if (rental.RentalType == RentalType.Hourly)
            {
                return Math.Max(
                    1,
                    rental.HoursRented
                    ?? (int)Math.Ceiling((rental.EndDateUtc - rental.StartDateUtc).TotalHours));
            }

            // Cennik dzienny działa według polskiego kalendarza, nie długości UTC.
            // Dzięki temu doba obejmująca zmianę czasu nadal jest jednym dniem najmu.
            return Math.Max(1, (int)Math.Ceiling((endsAtLocal - startsAtLocal).TotalDays));
        }

        private static decimal ResolveDisplayedUnitPrice(
            Rental rental,
            RentalItem item,
            int billedUnits)
        {
            if (item.Quantity > 0 && billedUnits > 0 && item.Subtotal >= 0)
            {
                return Math.Round(
                    item.Subtotal / item.Quantity / billedUnits,
                    2,
                    MidpointRounding.AwayFromZero);
            }

            return rental.RentalType == RentalType.Hourly
                ? item.PricePerHour ?? item.PricePerDay
                : item.PricePerDay;
        }

        private static string FormatRentalDuration(RentalType rentalType, int billedUnits)
        {
            if (rentalType == RentalType.Daily)
                return billedUnits == 1 ? "1 dzień" : $"{billedUnits} dni";

            var lastTwo = billedUnits % 100;
            var last = billedUnits % 10;
            var unit = billedUnits == 1
                ? "godzina"
                : last is >= 2 and <= 4 && lastTwo is not (>= 12 and <= 14)
                    ? "godziny"
                    : "godzin";
            return $"{billedUnits} {unit}";
        }

        private static IReadOnlyList<string> BuildCompanyDetails(CompanyInfo companyInfo)
        {
            var details = BuildCompanyAddressLines(companyInfo)
                .Select(SanitizePdfText)
                .ToList();

            AddDetail(details, "NIP", companyInfo.NIP);
            AddDetail(details, "REGON", companyInfo.REGON);
            AddDetail(details, "Telefon", companyInfo.PhoneNumber);
            AddDetail(details, "E-mail", companyInfo.Email);
            return details;
        }

        private static IReadOnlyList<string> BuildCustomerDetails(Customer customer)
        {
            var details = new List<string>();
            AddDetail(details, "Dokument", customer.DocumentNumber);
            AddDetail(details, "Adres", customer.Address);
            AddDetail(details, "Telefon", customer.PhoneNumber);
            AddDetail(details, "E-mail", customer.Email);
            return details;
        }

        private static void AddDetail(ICollection<string> details, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                details.Add($"{label}: {SanitizePdfText(value)}");
        }

        private async Task<ContractLogo?> LoadLogoAsync(Tenant? tenant, CancellationToken ct)
        {
            if (tenant == null)
                return null;

            var storagePath = ExtractLogoStoragePath(tenant.LogoUrl, tenant.Id);
            if (storagePath == null)
                return null;
            if (storagePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Logo SVG tenanta {TenantId} nie jest osadzane w PDF; użyto nazwy firmy",
                    tenant!.Id);
                return null;
            }

            try
            {
                var bytes = await _fileStorage.ReadAsync(storagePath, ct);
                if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024)
                {
                    _logger.LogWarning(
                        "Pominięto logo w umowie tenanta {TenantId}: nieprawidłowy rozmiar",
                        tenant!.Id);
                    return null;
                }

                if (!LooksLikeRasterImage(bytes) || !HasSafeRasterDimensions(bytes))
                {
                    _logger.LogWarning(
                        "Pominięto logo w umowie tenanta {TenantId}: nierozpoznany format obrazu",
                        tenant!.Id);
                    return null;
                }

                // QuestPDF utrzymuje globalny cache obrazów. Dokument musi dostać
                // własny bufor, bo ponowne użycie tej samej tablicy między dwoma
                // generacjami potrafi uszkodzić XObject w kolejnym PDF-ie.
                return new ContractLogo(bytes.ToArray());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Nie udało się pobrać logo tenanta {TenantId} do umowy; użyto nazwy firmy",
                    tenant!.Id);
                return null;
            }
        }

        private static RentalContractBranding ResolveBranding(Tenant? tenant, ContractLogo? logo)
        {
            const string fallbackPrimary = "#2F3C7E";
            const string fallbackSecondary = "#F96167";

            var primary = NormalizeHexColor(tenant?.PrimaryColorHex) ?? fallbackPrimary;
            var secondary = NormalizeHexColor(tenant?.SecondaryColorHex) ?? fallbackSecondary;
            if (string.Equals(primary, secondary, StringComparison.OrdinalIgnoreCase))
                secondary = string.Equals(primary, fallbackSecondary, StringComparison.OrdinalIgnoreCase)
                    ? fallbackPrimary
                    : fallbackSecondary;

            return new RentalContractBranding(
                primary,
                secondary,
                GetContrastingTextColor(primary),
                GetReadableInkColor(primary, fallbackPrimary),
                GetReadableInkColor(secondary, GetReadableInkColor(primary, fallbackPrimary)),
                logo?.Bytes);
        }

        private static string? NormalizeHexColor(string? value)
            => !string.IsNullOrWhiteSpace(value)
               && System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$")
                ? value.ToUpperInvariant()
                : null;

        private static string GetContrastingTextColor(string background)
        {
            const string ink = "#182230";
            const string white = "#FFFFFF";
            return ContrastRatio(background, white) >= ContrastRatio(background, ink)
                ? white
                : ink;
        }

        private static string GetReadableInkColor(string candidate, string fallback)
            => ContrastRatio(candidate, "#FFFFFF") >= 4.5d ? candidate : fallback;

        private static double ContrastRatio(string first, string second)
        {
            var firstLuminance = RelativeLuminance(first);
            var secondLuminance = RelativeLuminance(second);
            var lighter = Math.Max(firstLuminance, secondLuminance);
            var darker = Math.Min(firstLuminance, secondLuminance);
            return (lighter + 0.05d) / (darker + 0.05d);
        }

        private static double RelativeLuminance(string color)
        {
            var channels = new[] { 1, 3, 5 }
                .Select(index => Convert.ToInt32(color.Substring(index, 2), 16) / 255d)
                .Select(channel => channel <= 0.04045d
                    ? channel / 12.92d
                    : Math.Pow((channel + 0.055d) / 1.055d, 2.4d))
                .ToArray();

            return (0.2126d * channels[0]) + (0.7152d * channels[1]) + (0.0722d * channels[2]);
        }

        private static string? ExtractLogoStoragePath(string? logoUrl, Guid tenantId)
        {
            if (string.IsNullOrWhiteSpace(logoUrl))
                return null;

            var path = logoUrl;
            if (Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri))
                path = Uri.UnescapeDataString(uri.AbsolutePath);

            path = path.Replace('\\', '/');
            const string marker = "images/tenants/";
            var markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                return null;

            var storagePath = path[markerIndex..].TrimStart('/');
            if (storagePath.Split('/').Any(segment => segment is "." or ".."))
                return null;
            var expectedPrefix = $"images/tenants/{tenantId:D}/";
            if (!storagePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            return storagePath;
        }

        private static bool LooksLikeRasterImage(byte[] bytes)
        {
            var isPng = bytes.Length >= 8
                        && bytes[0] == 0x89
                        && bytes[1] == 0x50
                        && bytes[2] == 0x4E
                        && bytes[3] == 0x47;
            var isJpeg = bytes.Length >= 3
                         && bytes[0] == 0xFF
                         && bytes[1] == 0xD8
                         && bytes[2] == 0xFF;
            var isWebP = bytes.Length >= 12
                         && bytes[0] == (byte)'R'
                         && bytes[1] == (byte)'I'
                         && bytes[2] == (byte)'F'
                         && bytes[3] == (byte)'F'
                         && bytes[8] == (byte)'W'
                         && bytes[9] == (byte)'E'
                         && bytes[10] == (byte)'B'
                         && bytes[11] == (byte)'P';
            return isPng || isJpeg || isWebP;
        }

        private static bool HasSafeRasterDimensions(byte[] bytes)
        {
            const int maxDimension = 8192;
            const long maxPixels = 20_000_000;

            try
            {
                using var stream = new SkiaSharp.SKMemoryStream(bytes);
                using var codec = SkiaSharp.SKCodec.Create(stream);
                if (codec == null)
                    return false;

                var width = codec.Info.Width;
                var height = codec.Info.Height;
                return width is > 0 and <= maxDimension
                       && height is > 0 and <= maxDimension
                       && (long)width * height <= maxPixels;
            }
            catch
            {
                return false;
            }
        }

        private static IReadOnlyList<string> ParseContractTerms(string content)
        {
            var rawLines = content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .ToList();

            var termsStart = rawLines.FindIndex(IsTermsSectionHeading);
            if (termsStart >= 0)
            {
                var termsEnd = rawLines.FindIndex(
                    termsStart + 1,
                    line => line.Contains("PODPIS", StringComparison.OrdinalIgnoreCase));
                rawLines = rawLines
                    .Skip(termsStart + 1)
                    .Take(termsEnd > termsStart ? termsEnd - termsStart - 1 : rawLines.Count)
                    .ToList();
            }

            var terms = new List<string>();
            var hasNumberedTerms = false;
            foreach (var rawLine in rawLines)
            {
                if (string.IsNullOrWhiteSpace(rawLine) || IsSeparatorLine(rawLine))
                    continue;

                var line = SanitizePdfText(rawLine).Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var match = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"^\d+[.)]\s*(.+)$",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    terms.Add(match.Groups[1].Value.Trim());
                    hasNumberedTerms = true;
                    continue;
                }

                if (hasNumberedTerms && terms.Count > 0)
                {
                    terms[^1] = $"{terms[^1]} {line}".Trim();
                    continue;
                }

                if (!LooksLikeDocumentHeading(line))
                    terms.Add(line);
            }

            return terms.Count > 0
                ? terms
                : ParseContractTerms(ContractTemplateDefaults.StandardTerms);
        }

        private static string ResolveTermsContent(string templateContent, string filledTemplate)
        {
            var templateLines = templateContent
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

            if (templateLines.Any(IsTermsSectionHeading))
                return filledTemplate;

            // Starszy edytor zapisywał cały dokument tekstowy z danymi stron,
            // tabelą i podpisami. Nowy renderer tworzy te sekcje sam, dlatego
            // nie wolno wkleić całego starego szablonu ponownie jako warunków.
            // Własny tekst bez pól układu nadal jest traktowany jako warunki.
            if (LooksLikeLegacyFullDocumentTemplate(templateContent))
                return ContractTemplateDefaults.StandardTerms;

            return filledTemplate;
        }

        private static bool IsTermsSectionHeading(string line)
            => line.Contains("WARUNKI UMOWY", StringComparison.OrdinalIgnoreCase)
               || line.Contains("POSTANOWIENIA UMOWY", StringComparison.OrdinalIgnoreCase);

        private static bool LooksLikeLegacyFullDocumentTemplate(string content)
        {
            if (content.Contains("{{ItemsTable}}", StringComparison.OrdinalIgnoreCase))
                return true;

            var layoutVariables = new[]
            {
                "{{CompanyName}}",
                "{{CustomerName}}",
                "{{StartDate}}",
                "{{EndDate}}",
                "{{Total}}",
                "{{Deposit}}"
            };
            return layoutVariables.Count(variable =>
                content.Contains(variable, StringComparison.OrdinalIgnoreCase)) >= 4;
        }

        private static bool IsSeparatorLine(string line)
            => line.All(character =>
                char.IsWhiteSpace(character)
                || character is '═' or '─' or '━' or '_' or '-' or '=');

        private static bool LooksLikeDocumentHeading(string line)
        {
            var normalized = line.Trim().Trim(':');
            return normalized.Equals("UMOWA WYPOŻYCZENIA SPRZĘTU SPORTOWEGO", StringComparison.OrdinalIgnoreCase)
                   || normalized.Equals("STRONY UMOWY", StringComparison.OrdinalIgnoreCase)
                   || normalized.Equals("OKRES WYPOŻYCZENIA", StringComparison.OrdinalIgnoreCase)
                   || normalized.Equals("WYPOŻYCZONY SPRZĘT", StringComparison.OrdinalIgnoreCase)
                   || normalized.Equals("PŁATNOŚCI", StringComparison.OrdinalIgnoreCase)
                   || normalized.Equals("PODPISY", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizePdfText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new System.Text.StringBuilder(value.Length);
            foreach (var rune in value.EnumerateRunes())
            {
                var codePoint = rune.Value;
                var unsupported = codePoint is >= 0x2500 and <= 0x257F
                                  or >= 0x2600 and <= 0x27BF
                                  or >= 0x1F300 and <= 0x1FAFF
                                  or 0xFE0F;
                if (!unsupported)
                    builder.Append(rune.ToString());
            }

            return builder.ToString();
        }

        private sealed record ContractLogo(byte[] Bytes);

        private static string FillTemplate(
            string templateContent,
            Rental rental,
            IEnumerable<RentalItem> items,
            Customer customer,
            IReadOnlyDictionary<Guid, Product> productMap,
            CompanyInfo? companyInfo)
        {
            var pl = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
            var startsAt = PolishTimeZone.FromUtc(rental.StartDateUtc);
            var endsAt = PolishTimeZone.FromUtc(rental.EndDateUtc);
            var billedUnits = GetBilledUnits(rental, startsAt, endsAt);
            var duration = FormatRentalDuration(rental.RentalType, billedUnits);
            var priceUnit = rental.RentalType == RentalType.Hourly ? "godz." : "dzień";
            var itemsLines = string.Join("\n", items.Select(it =>
            {
                var p = productMap.GetValueOrDefault(it.ProductId);
                var displayedPrice = ResolveDisplayedUnitPrice(rental, it, billedUnits);
                return $"- {(p?.Name ?? it.ProductId.ToString())} x{it.Quantity} @ " +
                       $"{displayedPrice.ToString("N2", pl)} zł/{priceUnit} = {it.Subtotal.ToString("N2", pl)} zł";
            }));
            var rentalDays = Math.Max(1, (int)Math.Ceiling((endsAt - startsAt).TotalDays));
            var companyAddress = companyInfo == null
                ? ""
                : string.Join(", ", BuildCompanyAddressLines(companyInfo));

            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["{{CustomerName}}"] = customer.FullName,
                ["{{CustomerDocument}}"] = customer.DocumentNumber ?? "",
                ["{{CustomerEmail}}"] = customer.Email ?? "",
                ["{{CustomerPhone}}"] = customer.PhoneNumber ?? "",
                ["{{CustomerAddress}}"] = customer.Address ?? "",
                ["{{StartDate}}"] = startsAt.ToString("dd.MM.yyyy HH:mm", pl),
                ["{{EndDate}}"] = endsAt.ToString("dd.MM.yyyy HH:mm", pl),
                ["{{RentalDays}}"] = rentalDays.ToString(pl),
                ["{{RentalHours}}"] = rental.RentalType == RentalType.Hourly ? billedUnits.ToString(pl) : "",
                ["{{RentalDuration}}"] = duration,
                ["{{PriceUnit}}"] = priceUnit,
                ["{{RentalId}}"] = rental.Id.ToString()[..8].ToUpperInvariant(),
                ["{{CurrentDate}}"] = PolishTimeZone.FromUtc(DateTime.UtcNow).ToString("dd.MM.yyyy", pl),
                ["{{ItemsTable}}"] = itemsLines,
                ["{{Total}}"] = rental.TotalAmount.ToString("N2", pl),
                ["{{Deposit}}"] = rental.DepositAmount.ToString("N2", pl),
                ["{{CompanyName}}"] = companyInfo?.Name ?? "RentSpot",
                ["{{CompanyAddress}}"] = companyAddress,
                ["{{CompanyPostalCode}}"] = companyInfo?.PostalCode ?? "",
                ["{{CompanyCity}}"] = companyInfo?.City ?? "",
                ["{{CompanyVoivodeship}}"] = companyInfo?.Voivodeship ?? "",
                ["{{CompanyNIP}}"] = companyInfo?.NIP ?? "",
                ["{{CompanyREGON}}"] = companyInfo?.REGON ?? "",
                ["{{CompanyPhone}}"] = companyInfo?.PhoneNumber ?? "",
                ["{{CompanyEmail}}"] = companyInfo?.Email ?? ""
            };

            return System.Text.RegularExpressions.Regex.Replace(
                templateContent,
                @"\{\{[A-Za-z][A-Za-z0-9_]*\}\}",
                match => variables.GetValueOrDefault(match.Value, match.Value),
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
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
            var startsAt = PolishTimeZone.FromUtc(rental.StartDateUtc);
            var endsAt = PolishTimeZone.FromUtc(rental.EndDateUtc);
            var billedUnits = GetBilledUnits(rental, startsAt, endsAt);
            var rentalDuration = FormatRentalDuration(rental.RentalType, billedUnits);
            var companyName = companyInfo?.Name ?? "RentSpot";

            var htmlBody = GenerateConfirmationEmailHtml(
                rental,
                items.ToList(),
                customer,
                productMap,
                companyInfo,
                billedUnits,
                rentalDuration);
            
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

        private string GenerateConfirmationEmailHtml(
            Rental rental,
            List<RentalItem> items,
            Customer customer,
            Dictionary<Guid, Product> productMap,
            CompanyInfo? companyInfo,
            int billedUnits,
            string rentalDuration)
        {
            var companyName = HtmlEncode(companyInfo?.Name ?? "RentSpot Partner");
            var companyEmail = HtmlEncode(companyInfo?.Email ?? "kontakt@rentspot.eu");
            var companyPhone = HtmlEncode(companyInfo?.PhoneNumber ?? "");
            var companyAddress = HtmlEncode(companyInfo?.Address ?? "");
            var customerName = HtmlEncode(customer.FullName);
            var startsAt = PolishTimeZone.FromUtc(rental.StartDateUtc);
            var endsAt = PolishTimeZone.FromUtc(rental.EndDateUtc);
            var branding = ResolveBranding(companyInfo?.Tenant, logo: null);
            var primaryColor = branding.PrimaryColor;
            var secondaryColor = branding.SecondaryColor;
            var headerTextColor = branding.PrimaryTextColor;
            var emailLogoUrl = GetEmailLogoUrl(companyInfo?.Tenant?.LogoUrl);
            var priceUnitLabel = rental.RentalType == RentalType.Hourly ? "Cena/godz." : "Śr. cena/dzień";
            var brandHeader = emailLogoUrl == null
                ? $"<h1 style='margin: 0; font-size: 26px; font-weight: 700; color: {headerTextColor};'>{companyName}</h1>"
                : $"<img src='{HtmlEncode(emailLogoUrl)}' alt='{companyName}' style='display: block; max-width: 210px; max-height: 72px; margin: 0 auto 10px auto;' /><h1 style='margin: 0; font-size: 20px; font-weight: 700; color: {headerTextColor};'>{companyName}</h1>";
            
            var itemsHtml = string.Join("", items.Select(it =>
            {
                var p = productMap.GetValueOrDefault(it.ProductId);
                var displayedPrice = ResolveDisplayedUnitPrice(rental, it, billedUnits);
                return $@"
                    <tr>
                        <td style='padding: 12px; border-bottom: 1px solid #e5e7eb;'><strong>{HtmlEncode(p?.Name ?? "Produkt")}</strong></td>
                        <td style='padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: center;'>{it.Quantity}</td>
                        <td style='padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: right;'>{displayedPrice:0.00} zł</td>
                        <td style='padding: 12px; border-bottom: 1px solid #e5e7eb; text-align: right; color: {primaryColor}; font-weight: 600;'>{it.Subtotal:0.00} zł</td>
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
        <div style='background-color: {primaryColor}; border-bottom: 6px solid {secondaryColor}; padding: 30px 20px; text-align: center;'>
            {brandHeader}
            <p style='margin-top: 8px; font-size: 14px; color: {headerTextColor}; opacity: 0.9;'>Dziękujemy za wypożyczenie!</p>
        </div>
        
        <!-- Content -->
        <div style='padding: 30px 20px;'>
            <div style='background-color: #10b981; color: white; padding: 10px 20px; border-radius: 25px; display: inline-block; font-weight: 600; font-size: 14px; margin-bottom: 20px;'>
                ✓ Rezerwacja potwierdzona
            </div>
            
            <p style='font-size: 16px; margin-bottom: 24px;'>
                Cześć <strong>{customerName}</strong>,
            </p>
            
            <p style='margin-bottom: 24px;'>
                Twoja rezerwacja została potwierdzona! W załączniku znajdziesz umowę wypożyczenia w formacie PDF.
            </p>

            <!-- Szczegóły rezerwacji -->
            <div style='margin-bottom: 30px;'>
                <h3 style='font-size: 16px; font-weight: 600; color: #1f2937; margin-bottom: 12px; border-bottom: 2px solid #e5e7eb; padding-bottom: 8px;'>📅 Szczegóły rezerwacji</h3>
                <div style='background-color: #f9fafb; border-left: 4px solid {primaryColor}; padding: 16px; border-radius: 4px;'>
                    <table style='width: 100%;'>
                        <tr>
                            <td style='padding: 6px 0; font-weight: 600; color: #6b7280;'>Numer rezerwacji:</td>
                            <td style='padding: 6px 0; text-align: right;'><strong>#{rental.Id.ToString()[..8].ToUpper()}</strong></td>
                        </tr>
                        <tr>
                            <td style='padding: 6px 0; font-weight: 600; color: #6b7280;'>Data rozpoczęcia:</td>
                            <td style='padding: 6px 0; text-align: right;'>{startsAt:dd MMMM yyyy, HH:mm}</td>
                        </tr>
                        <tr>
                            <td style='padding: 6px 0; font-weight: 600; color: #6b7280;'>Data zakończenia:</td>
                            <td style='padding: 6px 0; text-align: right;'>{endsAt:dd MMMM yyyy, HH:mm}</td>
                        </tr>
                        <tr>
                            <td style='padding: 6px 0; font-weight: 600; color: #6b7280;'>Czas najmu:</td>
                            <td style='padding: 6px 0; text-align: right;'><strong>{rentalDuration}</strong></td>
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
                            <th style='padding: 12px; text-align: right; font-weight: 600; color: #374151;'>{priceUnitLabel}</th>
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
                            <td style='padding: 6px 0; text-align: right; color: {primaryColor}; font-weight: 600;'>{rental.DepositAmount:0.00} zł</td>
                        </tr>" : "")}
                        <tr style='border-top: 2px solid #e5e7eb;'>
                            <td style='padding: 12px 0 6px 0; font-weight: 700; font-size: 16px;'>RAZEM DO ZAPŁATY:</td>
                            <td style='padding: 12px 0 6px 0; text-align: right; font-weight: 700; font-size: 16px; color: {primaryColor};'>{rental.TotalAmount:0.00} zł</td>
                        </tr>
                    </table>
                </div>
            </div>

            <!-- Ważne informacje -->
            <div style='background-color: #fef3c7; border-left: 4px solid #f59e0b; padding: 16px; border-radius: 4px; margin-bottom: 30px;'>
                <p style='margin: 0; font-weight: 600;'>ℹ️ Ważne informacje:</p>
                <ul style='margin: 12px 0 0 0; padding-left: 20px;'>
                    <li>Stawić się w punkcie wypożyczalni w dniu <strong>{startsAt:dd.MM.yyyy}</strong></li>
                    <li>Zabrać ze sobą <strong>dokument tożsamości</strong></li>
                    <li>Sprawdzić stan sprzętu przy odbiorze</li>
                </ul>
            </div>

            <!-- Kontakt -->
            <div style='text-align: center; margin-top: 30px;'>
                <p style='color: #6b7280; font-size: 14px;'>
                    W razie pytań, skontaktuj się z nami:<br>
                    {(string.IsNullOrWhiteSpace(companyEmail) ? "" : $"📧 <a href='mailto:{companyEmail}' style='color: {primaryColor};'>{companyEmail}</a><br>")}
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

        private static string HtmlEncode(string? value)
            => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

        private static string? GetEmailLogoUrl(string? logoUrl)
        {
            if (!Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri))
                return null;

            return uri.Scheme is "http" or "https" ? uri.ToString() : null;
        }
    }
}
