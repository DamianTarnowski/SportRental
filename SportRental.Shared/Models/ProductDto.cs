namespace SportRental.Shared.Models
{
    public class ProductsPagedResponse
    {
        public List<ProductDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
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
            if (MinPrice.HasValue) parts.Add($"minPrice={MinPrice.Value}");
            if (MaxPrice.HasValue) parts.Add($"maxPrice={MaxPrice.Value}");
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
        public decimal DailyPrice { get; set; }
        public decimal? HourlyPrice { get; set; }  // Cena za godzinę (opcjonalna)
        
        // Dodatkowe właściwości dla klienta
        public string? Description { get; set; }
        public string? FullImageUrl { get; set; }
        public bool IsAvailable { get; set; } = true;
        public int AvailableQuantity { get; set; }
        
        // Location
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
                return GetPlaceholderImage(width);

            // Check if ImageUrl is a full URL (Azure Blob, CDN, etc.)
            if (ImageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                ImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Replace filename with requested width variant
                var ext = System.IO.Path.GetExtension(ImageUrl);
                var urlWithoutFilename = ImageUrl.Substring(0, ImageUrl.LastIndexOf('/') + 1);
                return $"{urlWithoutFilename}w{width}{ext}";
            }

            // Fallback to ImageUrl
            return ImageUrl;
        }

        // Get original full-size image URL
        public string GetOriginalImageUrl()
        {
            if (string.IsNullOrWhiteSpace(ImageUrl))
                return string.Empty;

            // Check if ImageUrl is a full URL (Azure Blob, CDN, etc.)
            if (ImageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                ImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Replace w800/w400/w1280 with original
                var ext = System.IO.Path.GetExtension(ImageUrl);
                var urlWithoutFilename = ImageUrl.Substring(0, ImageUrl.LastIndexOf('/') + 1);
                return $"{urlWithoutFilename}original{ext}";
            }

            // Fallback to ImageUrl
            return ImageUrl;
        }

        public string GetImageSrcSet()
        {
            if (string.IsNullOrWhiteSpace(ImageUrl))
                return string.Empty;

            var ext = System.IO.Path.GetExtension(ImageUrl);
            
            // Check if ImageUrl is a full URL (Azure Blob, CDN, etc.)
            if (!string.IsNullOrEmpty(ImageUrl) &&
                (ImageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                ImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                // Build srcset with full URLs
                var lastSlashIndex = ImageUrl.LastIndexOf('/');
                if (lastSlashIndex > 0)
                {
                    var urlWithoutFilename = ImageUrl.Substring(0, lastSlashIndex + 1);
                    return $"{urlWithoutFilename}w400{ext} 400w, {urlWithoutFilename}w800{ext} 800w, {urlWithoutFilename}w1280{ext} 1280w";
                }
            }

            return string.Empty;
        }

        private string GetPlaceholderImage(int width = 800)
        {
            // Placeholder images based on category from Unsplash
            var categoryPlaceholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Narty"] = $"https://images.unsplash.com/photo-1551698618-1dfe5d97d256?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Snowboard"] = $"https://images.unsplash.com/photo-1519315901367-224f0c3e6c01?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Rowery miejskie"] = $"https://images.unsplash.com/photo-1571068316344-75bc76f77890?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Rowery elektryczne"] = $"https://images.unsplash.com/photo-1591227080018-acef3b3651e7?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["MTB"] = $"https://images.unsplash.com/photo-1576435728678-68d0fbf94e91?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["SUP"] = $"https://images.unsplash.com/photo-1595433707802-6b2626ef1c91?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Windsurfing"] = $"https://images.unsplash.com/photo-1537519646099-335112e5f70f?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Kitesurfing"] = $"https://images.unsplash.com/photo-1559827260-dc66d52bef19?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Buty"] = $"https://images.unsplash.com/photo-1542291026-7eec264c27ff?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Kaski"] = $"https://images.unsplash.com/photo-1590546637310-6f8f5b5cd1e7?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Gogle"] = $"https://images.unsplash.com/photo-1588731247985-c952b0e9b9db?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Pianki"] = $"https://images.unsplash.com/photo-1559827260-dc66d52bef19?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Akcesoria"] = $"https://images.unsplash.com/photo-1523275335684-37898b6baf30?w={width}&h={width * 3 / 4}&fit=crop&q=80",
                ["Bezpieczeństwo"] = $"https://images.unsplash.com/photo-1558618666-fcd25c85cd64?w={width}&h={width * 3 / 4}&fit=crop&q=80"
            };

            if (!string.IsNullOrEmpty(Category) && categoryPlaceholders.TryGetValue(Category, out var placeholder))
            {
                return placeholder;
            }

            // Default sports placeholder
            return $"https://images.unsplash.com/photo-1461896836934-ffe607ba8211?w={width}&h={width * 3 / 4}&fit=crop&q=80";
        }
    }
}