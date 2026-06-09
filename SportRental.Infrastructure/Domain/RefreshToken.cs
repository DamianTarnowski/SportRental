namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Refresh token stored in database for secure token rotation.
///
/// SEC-010 NOTE (2026-06): scaffold istnieje od grudnia 2025, ale aktualnie
/// NIE jest używany w kodzie (zero referencji do RefreshTokens.Add/Find w aktywnym flow).
/// Jeśli kiedyś zaczniesz używać: NIE zapisuj plaintext Token w DB.
/// Zapisz SHA-256 hash + jakąś sól (np. Id-as-salt), w cookie/klient daj plaintext,
/// w refresh-endpoincie porównaj hash przez CryptographicOperations.FixedTimeEquals.
/// Analogicznie do SmsConfirmationService.HashCode (SEC-012 fix).
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsRevoked { get; set; }
    public string? RevokedReason { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByToken { get; set; }
    
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    public bool IsActive => !IsRevoked && !IsExpired;
}
