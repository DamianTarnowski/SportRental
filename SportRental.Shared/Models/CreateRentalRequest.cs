using System.ComponentModel.DataAnnotations;

namespace SportRental.Shared.Models;

public class CreateRentalRequest
{
    [Required]
    public Guid CustomerId { get; set; }

    [Required]
    public DateTime StartDateUtc { get; set; }

    [Required]
    public DateTime EndDateUtc { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateRentalItem> Items { get; set; } = new();

    [Required]
    [MaxLength(64)]
    public string PaymentIntentId { get; set; } = string.Empty;

    // Dodatkowe informacje od klienta
    public string? Notes { get; set; }

    // Idempotency support (optional)
    public string? IdempotencyKey { get; set; }

    // Typ wynajmu (godzinowy/dzienny)
    public RentalTypeDto RentalType { get; set; } = RentalTypeDto.Daily;
    public int? HoursRented { get; set; }  // Liczba godzin (tylko dla RentalType.Hourly)
}

public enum RentalTypeDto
{
    Daily = 0,
    Hourly = 1
}

public class CreateRentalItem
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}

public class RentalResponse
{
    public Guid Id { get; set; }
    public decimal TotalAmount { get; set; }
    public string? ContractUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal DepositAmount { get; set; }
    public string PaymentStatus { get; set; } = PaymentIntentStatus.Succeeded;
}
