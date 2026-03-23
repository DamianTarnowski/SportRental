using BarcodeStandard;
using SkiaSharp;

namespace SportRental.Admin.Services.QrCode;

public interface IBarcodeGenerator
{
    /// <summary>
    /// Generates a Code 128 barcode as a base64 data URL
    /// </summary>
    Task<string> GenerateBarcodeAsync(string data, int width = 300, int height = 80, CancellationToken ct = default);

    /// <summary>
    /// Generates a Code 128 barcode as PNG bytes
    /// </summary>
    Task<byte[]> GenerateBarcodeBytesAsync(string data, int width = 300, int height = 80, CancellationToken ct = default);

    /// <summary>
    /// Generates barcode data for a product (based on SKU or short ID)
    /// </summary>
    string GenerateProductBarcodeData(Guid productId, string? sku);
}

public class BarcodeGenerator : IBarcodeGenerator
{
    private readonly ILogger<BarcodeGenerator> _logger;

    public BarcodeGenerator(ILogger<BarcodeGenerator> logger)
    {
        _logger = logger;
    }

    public Task<string> GenerateBarcodeAsync(string data, int width = 300, int height = 80, CancellationToken ct = default)
    {
        try
        {
            var pngBytes = GenerateBarcodePng(data, width, height);
            var base64 = Convert.ToBase64String(pngBytes);
            _logger.LogDebug("Generated barcode for data: {Data}", data);
            return Task.FromResult($"data:image/png;base64,{base64}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate barcode for data: {Data}", data);
            throw;
        }
    }

    public Task<byte[]> GenerateBarcodeBytesAsync(string data, int width = 300, int height = 80, CancellationToken ct = default)
    {
        try
        {
            var pngBytes = GenerateBarcodePng(data, width, height);
            _logger.LogDebug("Generated barcode bytes for data: {Data}", data);
            return Task.FromResult(pngBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate barcode bytes for data: {Data}", data);
            throw;
        }
    }

    public string GenerateProductBarcodeData(Guid productId, string? sku)
    {
        // If SKU exists and is short enough for barcode, use it
        if (!string.IsNullOrWhiteSpace(sku) && sku.Length <= 20)
            return sku;

        // Otherwise use short product ID (first 8 chars of GUID)
        return $"SR{productId.ToString("N")[..8].ToUpper()}";
    }

    private static byte[] GenerateBarcodePng(string data, int width, int height)
    {
        var barcode = new Barcode
        {
            IncludeLabel = true,
            AlternateLabel = data,
            Width = width,
            Height = height,
            BackColor = SKColors.White,
            ForeColor = SKColors.Black
        };

        using var image = barcode.Encode(BarcodeStandard.Type.Code128, data);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
