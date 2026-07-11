using System.ComponentModel.DataAnnotations;

namespace SportRental.Shared.Models;

public sealed class ContactMessageRequest
{
    [NotEmptyGuid(ErrorMessage = "Wybierz wypożyczalnię.")]
    public Guid TenantId { get; set; }

    [Required(ErrorMessage = "Imię i nazwisko jest wymagane.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Imię i nazwisko musi mieć od 2 do 120 znaków.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Adres email jest wymagany.")]
    [EmailAddress(ErrorMessage = "Podaj poprawny adres email.")]
    [StringLength(200, ErrorMessage = "Adres email może mieć maksymalnie 200 znaków.")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Podaj poprawny numer telefonu.")]
    [StringLength(30, ErrorMessage = "Numer telefonu może mieć maksymalnie 30 znaków.")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Temat jest wymagany.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "Temat musi mieć od 3 do 150 znaków.")]
    public string Subject { get; set; } = string.Empty;

    [Required(ErrorMessage = "Wiadomość jest wymagana.")]
    [StringLength(4000, MinimumLength = 10, ErrorMessage = "Wiadomość musi mieć od 10 do 4000 znaków.")]
    public string Message { get; set; } = string.Empty;

}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotEmptyGuidAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is Guid id && id != Guid.Empty;
}
