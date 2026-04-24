using Microsoft.AspNetCore.DataProtection;

namespace SportRental.Admin.Services
{
    /// <summary>
    /// Generuje i waliduje tokeny do publicznej ankiety po zwrocie.
    /// Token zawiera RentalId + datę wygaśnięcia, zaszyfrowany DataProtection.
    /// Wystawiony mailowo w CTA "Wystaw opinię" — klient klika, trafia na /ankieta/{rentalId}?t={token}.
    /// Bez tokenu nie można wystawić opinii z publicznej strony.
    /// </summary>
    public interface IReviewSurveyTokenService
    {
        string Generate(Guid rentalId, TimeSpan? validFor = null);
        bool TryValidate(string token, Guid expectedRentalId, out DateTime expiresAtUtc);
    }

    public class ReviewSurveyTokenService : IReviewSurveyTokenService
    {
        private const string ProtectorPurpose = "RentalSurvey.v1";
        private static readonly TimeSpan DefaultValidity = TimeSpan.FromDays(60);

        private readonly IDataProtector _protector;

        public ReviewSurveyTokenService(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector(ProtectorPurpose);
        }

        public string Generate(Guid rentalId, TimeSpan? validFor = null)
        {
            var expires = DateTime.UtcNow.Add(validFor ?? DefaultValidity);
            var raw = $"{rentalId:N}|{expires.Ticks}";
            return _protector.Protect(raw);
        }

        public bool TryValidate(string token, Guid expectedRentalId, out DateTime expiresAtUtc)
        {
            expiresAtUtc = default;
            if (string.IsNullOrWhiteSpace(token)) return false;

            string raw;
            try
            {
                raw = _protector.Unprotect(token);
            }
            catch
            {
                return false;
            }

            var parts = raw.Split('|');
            if (parts.Length != 2) return false;
            if (!Guid.TryParseExact(parts[0], "N", out var rentalId)) return false;
            if (rentalId != expectedRentalId) return false;
            if (!long.TryParse(parts[1], out var ticks)) return false;

            expiresAtUtc = new DateTime(ticks, DateTimeKind.Utc);
            return expiresAtUtc > DateTime.UtcNow;
        }
    }
}
