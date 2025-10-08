using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;

namespace SportRental.Admin.Services.Storage
{
    public sealed class ImageVariantService
    {
        private readonly IFileStorage _storage;
        private readonly ILogger<ImageVariantService> _logger;

        // Responsive image sizes for different devices
        private static readonly int[] ImageWidths = new[] { 400, 800, 1280 };

        public ImageVariantService(IFileStorage storage, IConfiguration config, ILogger<ImageVariantService> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        public async Task<(string basePath, string defaultUrl, Dictionary<int, string> variants)> SaveProductImageAsync(
            Guid tenantId, 
            Guid productId, 
            string fileName, 
            Stream content, 
            CancellationToken ct = default)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var version = 1;
            var basePath = $"images/products/{tenantId}/{productId}/v{version}";
            
            // Load image
            using var image = await Image.LoadAsync(content, ct);
            var originalWidth = image.Width;
            var originalHeight = image.Height;
            
            _logger.LogInformation("Loaded image: {Width}x{Height}", originalWidth, originalHeight);

            // Save original
            var originalPath = $"{basePath}/original{ext}";
            using (var originalStream = new MemoryStream())
            {
                await SaveImageToStreamAsync(image, originalStream, ext);
                var originalBytes = originalStream.ToArray();
                await _storage.SaveAsync(originalPath, originalBytes, ct);
                _logger.LogInformation("Saved original: {Path}, Size: {Size}KB", originalPath, originalBytes.Length / 1024);
            }

            // Create and save variants
            var variants = new Dictionary<int, string>();
            string defaultUrl = string.Empty;

            foreach (var width in ImageWidths)
            {
                // Skip if original is smaller than target width
                if (originalWidth <= width && width != 800)
                {
                    _logger.LogInformation("Skipping variant w{Width} (original is {OriginalWidth}px)", width, originalWidth);
                    continue;
                }

                var variantPath = $"{basePath}/w{width}{ext}";
                
                // Clone image for resizing
                using var resized = image.Clone(ctx =>
                {
                    // Calculate height maintaining aspect ratio
                    var targetHeight = (int)(originalHeight * ((double)width / originalWidth));
                    ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(width, targetHeight),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3 // High quality resampling
                    });
                });

                using (var variantStream = new MemoryStream())
                {
                    await SaveImageToStreamAsync(resized, variantStream, ext);
                    var variantBytes = variantStream.ToArray();
                    var variantUrl = await _storage.SaveAsync(variantPath, variantBytes, ct);
                    variants[width] = variantUrl;
                    
                    _logger.LogInformation("Saved variant w{Width}: {Path}, Size: {Size}KB", 
                        width, variantPath, variantBytes.Length / 1024);

                    // Use w800 as default
                    if (width == 800)
                    {
                        defaultUrl = variantUrl;
                    }
                }
            }

            // If no w800 was created (image too small), use original
            if (string.IsNullOrEmpty(defaultUrl) && variants.Any())
            {
                defaultUrl = variants.Values.First();
            }

            return (basePath, defaultUrl, variants);
        }

        private static async Task SaveImageToStreamAsync(Image image, Stream stream, string extension)
        {
            // Save with appropriate format and quality
            if (extension == ".jpg" || extension == ".jpeg")
            {
                await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 85 });
            }
            else if (extension == ".webp")
            {
                await image.SaveAsWebpAsync(stream, new WebpEncoder { Quality = 85 });
            }
            else if (extension == ".png")
            {
                await image.SaveAsPngAsync(stream);
            }
            else
            {
                // Default to JPEG
                await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 85 });
            }
        }
    }
}


