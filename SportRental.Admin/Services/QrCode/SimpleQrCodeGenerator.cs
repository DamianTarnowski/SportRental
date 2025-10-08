using System.Text;
using System.Text.Json;

namespace SportRental.Admin.Services.QrCode
{
    public class SimpleQrCodeGenerator : IQrCodeGenerator
    {
        private readonly ILogger<SimpleQrCodeGenerator> _logger;

        public SimpleQrCodeGenerator(ILogger<SimpleQrCodeGenerator> logger)
        {
            _logger = logger;
        }

        public Task<string> GenerateQrCodeAsync(string data, int size = 200, CancellationToken cancellationToken = default)
        {
            // For now, return a simple data URL placeholder
            // In a real implementation, you would use a QR code library like QRCoder
            var encodedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
            var placeholder = $"data:text/plain;base64,{encodedData}";
            
            _logger.LogInformation("Generated QR code for data: {Data}", data.Length > 50 ? data.Substring(0, 50) + "..." : data);
            
            return Task.FromResult(placeholder);
        }

        public Task<byte[]> GenerateQrCodeBytesAsync(string data, int size = 200, CancellationToken cancellationToken = default)
        {
            // For now, return the data as bytes
            // In a real implementation, you would generate actual QR code image bytes
            var bytes = Encoding.UTF8.GetBytes(data);
            
            _logger.LogInformation("Generated QR code bytes for data: {Data}", data.Length > 50 ? data.Substring(0, 50) + "..." : data);
            
            return Task.FromResult(bytes);
        }

        public string GenerateProductQrCodeData(Guid productId, string productName, string sku)
        {
            var qrData = new
            {
                Type = "Product",
                ProductId = productId.ToString(),
                Name = productName,
                Sku = sku,
                GeneratedAt = DateTime.UtcNow.ToString("O")
            };

            return JsonSerializer.Serialize(qrData);
        }

        public string GenerateRentalQrCodeData(Guid rentalId, DateTime startDate, DateTime endDate)
        {
            var qrData = new
            {
                Type = "Rental",
                RentalId = rentalId.ToString(),
                StartDate = startDate.ToString("O"),
                EndDate = endDate.ToString("O"),
                GeneratedAt = DateTime.UtcNow.ToString("O")
            };

            return JsonSerializer.Serialize(qrData);
        }
    }
}