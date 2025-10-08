namespace SportRental.Shared.Models
{
    public class ProductDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? ImageUrl { get; set; }
        public string? ImageBasePath { get; set; } // For responsive images
        public decimal DailyPrice { get; set; }
        
        // Dodatkowe właściwości dla klienta
        public string? Description { get; set; }
        public string? FullImageUrl { get; set; }
        public bool IsAvailable { get; set; } = true;
        public int AvailableQuantity { get; set; }

        // Helper methods for responsive images
        public string GetImageUrl(int width = 800)
        {
            if (string.IsNullOrWhiteSpace(ImageUrl))
                return string.Empty;

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
    }
}