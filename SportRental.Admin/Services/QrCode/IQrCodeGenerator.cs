namespace SportRental.Admin.Services.QrCode
{
    /// <summary>
    /// BEZPIECZEŃSTWO: kody QR zostały wyłączone z UI od kwietnia 2026 i zastąpione kodami
    /// kreskowymi Code 128 (<see cref="IBarcodeGenerator"/>). QR są zbyt łatwe do podmiany
    /// przez phishing/podstawienie — w środowisku wypożyczalni stosujemy barcodes.
    /// Interfejs i implementacje pozostają zarejestrowane na wypadek przyszłej potrzeby.
    /// </summary>
    public interface IQrCodeGenerator
    {
        Task<string> GenerateQrCodeAsync(string data, int size = 200, CancellationToken cancellationToken = default);
        Task<byte[]> GenerateQrCodeBytesAsync(string data, int size = 200, CancellationToken cancellationToken = default);
        string GenerateProductQrCodeData(Guid productId, string productName, string sku);
        string GenerateRentalQrCodeData(Guid rentalId, DateTime startDate, DateTime endDate);
    }
}