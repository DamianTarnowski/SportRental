using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    /// <summary>
    /// Globalna konfiguracja floating chat — jeden rekord (singleton). Modyfikuje SuperAdmin
    /// ze /admin/chat-settings. Steruje:
    ///  - który model jest domyślny dla nowych userów / wszystkich gdy wybór wyłączony,
    ///  - czy user może sam przełączać model w UI chatu.
    /// Brak query filtra TenantId — to ustawienie globalne, nie per-tenant.
    /// </summary>
    public class ChatSettings
    {
        /// <summary>Pojedyncza stała wartość Guid.Empty — singleton row.</summary>
        [Key]
        public Guid Id { get; set; } = Guid.Empty;

        [Required, MaxLength(64)]
        public string DefaultModel { get; set; } = "gpt-5.5";

        public bool AllowUserModelChoice { get; set; } = true;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? UpdatedBy { get; set; }
    }
}
