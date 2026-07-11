namespace SportRental.Shared.Models
{
    public class ProductsPagedResponse
    {
        public List<ProductDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public int AvailableCount { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal MinimumPrice { get; set; }
    }

    public class ProductFilterRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 12;
        public string? Search { get; set; }
        public string? Category { get; set; }
        public string? City { get; set; }
        public string? Voivodeship { get; set; }
        public string? Tenant { get; set; }
        public Guid? TenantId { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public bool? Available { get; set; }
        public string? Sort { get; set; }
        public double? UserLat { get; set; }
        public double? UserLon { get; set; }

        public string ToQueryString()
        {
            var parts = new List<string>
            {
                $"page={Page}",
                $"pageSize={PageSize}"
            };

            if (!string.IsNullOrWhiteSpace(Search)) parts.Add($"search={Uri.EscapeDataString(Search)}");
            if (!string.IsNullOrWhiteSpace(Category)) parts.Add($"category={Uri.EscapeDataString(Category)}");
            if (!string.IsNullOrWhiteSpace(City)) parts.Add($"city={Uri.EscapeDataString(City)}");
            if (!string.IsNullOrWhiteSpace(Voivodeship)) parts.Add($"voivodeship={Uri.EscapeDataString(Voivodeship)}");
            if (!string.IsNullOrWhiteSpace(Tenant)) parts.Add($"tenant={Uri.EscapeDataString(Tenant)}");
            if (TenantId.HasValue) parts.Add($"tenantId={TenantId.Value:D}");
            if (MinPrice.HasValue) parts.Add($"minPrice={MinPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (MaxPrice.HasValue) parts.Add($"maxPrice={MaxPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (Available.HasValue) parts.Add($"available={Available.Value}");
            if (!string.IsNullOrWhiteSpace(Sort)) parts.Add($"sort={Uri.EscapeDataString(Sort)}");
            if (UserLat.HasValue) parts.Add($"userLat={UserLat.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (UserLon.HasValue) parts.Add($"userLon={UserLon.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            return string.Join("&", parts);
        }
    }

    public class ProductDto
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; } // Identyfikator wypożyczalni
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? ImageUrl { get; set; }
        public string? ImageBasePath { get; set; } // For responsive images
        public int[]? ImageVariantWidths { get; set; }
        public bool? HasOriginalImage { get; set; }
        public decimal DailyPrice { get; set; }
        public decimal? HourlyPrice { get; set; }  // Cena za godzinę (opcjonalna)
        
        // Dodatkowe właściwości dla klienta
        public string? Description { get; set; }
        public string? FullImageUrl { get; set; }
        public bool IsAvailable { get; set; } = true;
        public int AvailableQuantity { get; set; }
        
        // Location
        public string? PickupAddress { get; set; }
        public string? City { get; set; }
        public string? Voivodeship { get; set; }
        public double? Lat { get; set; }
        public double? Lon { get; set; }
        
        // Tenant (wypożyczalnia)
        public string? TenantName { get; set; }

        // Helper methods for responsive images
        public string GetImageUrl(int width = 800)
        {
            if (string.IsNullOrWhiteSpace(ImageUrl))
                return string.Empty;

            var imageUrl = ImageUrl.Trim();
            var declaredWidths = GetDeclaredVariantWidths();
            if (declaredWidths.Length == 0)
                return imageUrl;

            var requestedWidth = Math.Max(1, width);
            var selectedWidth = declaredWidths.FirstOrDefault(candidate => candidate >= requestedWidth);
            if (selectedWidth == 0)
                selectedWidth = declaredWidths[^1];

            return TryReplaceKnownVariant(imageUrl, $"w{selectedWidth}", out var variantUrl)
                ? variantUrl
                : imageUrl;
        }

        // Get original full-size image URL
        public string GetOriginalImageUrl()
        {
            if (string.IsNullOrWhiteSpace(ImageUrl))
                return string.Empty;

            var imageUrl = ImageUrl.Trim();
            return HasOriginalImage == true &&
                   TryReplaceKnownVariant(imageUrl, "original", out var originalUrl)
                ? originalUrl
                : imageUrl;
        }

        public string GetImageSrcSet()
        {
            if (string.IsNullOrWhiteSpace(ImageUrl))
                return string.Empty;

            var imageUrl = ImageUrl.Trim();
            var declaredWidths = GetDeclaredVariantWidths();
            if (declaredWidths.Length < 2)
                return string.Empty;

            var candidates = new List<string>(declaredWidths.Length);
            foreach (var width in declaredWidths)
            {
                if (!TryReplaceKnownVariant(imageUrl, $"w{width}", out var variantUrl))
                    return string.Empty;

                candidates.Add($"{variantUrl} {width}w");
            }

            return string.Join(", ", candidates);
        }

        public string GetPickupDisplayText()
        {
            if (!string.IsNullOrWhiteSpace(PickupAddress))
                return PickupAddress.Trim();

            if (!string.IsNullOrWhiteSpace(City))
                return City.Trim();

            return "Adres odbioru do potwierdzenia";
        }

        private static bool TryReplaceKnownVariant(
            string imageUrl,
            string targetStem,
            out string rewrittenUrl)
        {
            rewrittenUrl = imageUrl;
            if (!IsAbsoluteHttpUrl(imageUrl))
                return false;

            var suffixIndex = imageUrl.IndexOfAny('?', '#');
            var path = suffixIndex >= 0 ? imageUrl[..suffixIndex] : imageUrl;
            var suffix = suffixIndex >= 0 ? imageUrl[suffixIndex..] : string.Empty;
            var lastSlashIndex = path.LastIndexOf('/');
            if (lastSlashIndex < 0 || lastSlashIndex == path.Length - 1)
                return false;

            var fileName = path[(lastSlashIndex + 1)..];
            var extension = System.IO.Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension))
                return false;

            var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            if (!IsKnownVariantStem(stem))
                return false;

            rewrittenUrl = $"{path[..(lastSlashIndex + 1)]}{targetStem}{extension}{suffix}";
            return true;
        }

        private static bool IsAbsoluteHttpUrl(string value) =>
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        private int[] GetDeclaredVariantWidths() =>
            ImageVariantWidths?
                .Where(width => width is 400 or 800 or 1280)
                .Distinct()
                .OrderBy(width => width)
                .ToArray()
            ?? [];

        private static bool IsKnownVariantStem(string stem) =>
            stem.Equals("w400", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("w800", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("w1280", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("original", StringComparison.OrdinalIgnoreCase);

    }
}
