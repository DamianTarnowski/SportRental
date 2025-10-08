namespace SportRental.Admin.Services.QrCode
{
    public interface IQrCodeGenerator
    {
        Task<string> GenerateQrCodeAsync(string data, int size = 200, CancellationToken cancellationToken = default);
        Task<byte[]> GenerateQrCodeBytesAsync(string data, int size = 200, CancellationToken cancellationToken = default);
        string GenerateProductQrCodeData(Guid productId, string productName, string sku);
        string GenerateRentalQrCodeData(Guid rentalId, DateTime startDate, DateTime endDate);
    }
}