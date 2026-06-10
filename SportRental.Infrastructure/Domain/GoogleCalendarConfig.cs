using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Faza 9c (Bookero parity): konfiguracja sync wynajmów do Google Calendar partnera.
/// Per tenant: refresh_token (OAuth) + ID kalendarza + flag czy aktywne.
/// Sync: po RentalCreated/Updated/Returned background tworzy event w kalendarzu.
///
/// SECURITY: RefreshToken trzymany jako plain string TUTAJ (pre-launch).
/// TODO po launchu: szyfrować przez DataProtection (per-tenant unique purpose).
/// </summary>
public class GoogleCalendarConfig
{
    [Key]
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>OAuth refresh_token od Google. Long-lived (nie wygasa bez revoke).</summary>
    [Required, MaxLength(512)]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>ID kalendarza w Google ("primary" = główny kalendarz konta).</summary>
    [Required, MaxLength(256)]
    public string CalendarId { get; set; } = "primary";

    /// <summary>Email konta Google które autoryzowało (do informacji w UI).</summary>
    [MaxLength(256)]
    public string? ConnectedEmail { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAtUtc { get; set; }

    /// <summary>Ilość wynajmów ostatnio zsynchronizowanych (rolling counter).</summary>
    public int SyncedCount { get; set; }

    public Tenant? Tenant { get; set; }
}

/// <summary>
/// Per-rental mapping na Google event ID — żeby update modyfikował istniejący event.
/// </summary>
public class GoogleCalendarEvent
{
    [Key]
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RentalId { get; set; }

    [Required, MaxLength(256)]
    public string EventId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
