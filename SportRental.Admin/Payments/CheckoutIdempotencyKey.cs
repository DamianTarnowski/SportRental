using System.Security.Cryptography;
using System.Text;

namespace SportRental.Admin.Payments;

internal static class CheckoutIdempotencyKey
{
    public static string Create(Guid customerId, IEnumerable<Guid> holdIds)
    {
        var canonical = $"{customerId:N}|{string.Join('|', holdIds.Order().Select(id => id.ToString("N")))}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"checkout:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
