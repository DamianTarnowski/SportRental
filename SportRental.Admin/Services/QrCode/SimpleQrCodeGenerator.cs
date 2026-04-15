using System.Text;
using System.Text.Json;
using QRCoder;

namespace SportRental.Admin.Services.QrCode
{
    // BEZPIECZEŃSTWO: Generowanie kodów QR jest zachowane w kodzie, ale wyłączone z UI.
    // Kody QR mogą być łatwo podmienione przez osoby trzecie (phishing, przekierowanie na złośliwe strony).
    // W środowisku wypożyczalni sprzętu sportowego stosujemy kody kreskowe Code 128 (BarcodeGenerator).
    // Klasa SimpleQrCodeGenerator pozostaje dostępna na wypadek przyszłej potrzeby.
    public class SimpleQrCodeGenerator : IQrCodeGenerator
    {
        private readonly ILogger<SimpleQrCodeGenerator> _logger;
        private const string PRODUCT_PREFIX = "SR:P:";
        private const string RENTAL_PREFIX = "SR:R:";

        public SimpleQrCodeGenerator(ILogger<SimpleQrCodeGenerator> logger)
        {
            _logger = logger;
        }

        public Task<string> GenerateQrCodeAsync(string data, int size = 200, CancellationToken cancellationToken = default)
        {
            try
            {
                var pixelsPerModule = Math.Max(1, size / 25); // Approximate module count
                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new PngByteQRCode(qrCodeData);
                var pngBytes = qrCode.GetGraphic(pixelsPerModule);
                var base64 = Convert.ToBase64String(pngBytes);
                
                _logger.LogDebug("Generated QR code for data: {Data}", data.Length > 50 ? data.Substring(0, 50) + "..." : data);
                
                return Task.FromResult($"data:image/png;base64,{base64}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate QR code for data: {Data}", data);
                throw;
            }
        }

        public Task<byte[]> GenerateQrCodeBytesAsync(string data, int size = 200, CancellationToken cancellationToken = default)
        {
            try
            {
                var pixelsPerModule = Math.Max(1, size / 25);
                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new PngByteQRCode(qrCodeData);
                var pngBytes = qrCode.GetGraphic(pixelsPerModule);
                
                _logger.LogDebug("Generated QR code bytes for data: {Data}", data.Length > 50 ? data.Substring(0, 50) + "..." : data);
                
                return Task.FromResult(pngBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate QR code bytes for data: {Data}", data);
                throw;
            }
        }

        public string GenerateProductQrCodeData(Guid productId, string productName, string sku)
        {
            // Simple format for easy scanning: SR:P:{productId}
            return $"{PRODUCT_PREFIX}{productId}";
        }

        public string GenerateRentalQrCodeData(Guid rentalId, DateTime startDate, DateTime endDate)
        {
            // Simple format for easy scanning: SR:R:{rentalId}
            return $"{RENTAL_PREFIX}{rentalId}";
        }
        
        /// <summary>
        /// Parses QR code data and returns the type and ID
        /// </summary>
        public static (string Type, Guid? Id, string? SkuOrCode) ParseQrCodeData(string qrData)
        {
            if (string.IsNullOrWhiteSpace(qrData))
                return (string.Empty, null, null);
            
            var data = qrData.Trim();
            
            if (data.StartsWith(PRODUCT_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                var idPart = data.Substring(PRODUCT_PREFIX.Length);
                if (Guid.TryParse(idPart, out var productId))
                    return ("Product", productId, null);
            }
            else if (data.StartsWith(RENTAL_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                var idPart = data.Substring(RENTAL_PREFIX.Length);
                if (Guid.TryParse(idPart, out var rentalId))
                    return ("Rental", rentalId, null);
            }
            // Try to parse as raw GUID (backwards compatibility)
            else if (Guid.TryParse(data, out var rawId))
            {
                return ("Unknown", rawId, null);
            }
            
            // Barcode/SKU — not a GUID, treat as product SKU or barcode data
            return ("Barcode", null, data);
        }
    }
}
