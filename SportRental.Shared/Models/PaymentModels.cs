using System.ComponentModel.DataAnnotations;

namespace SportRental.Shared.Models;

public class PaymentQuoteRequest
{
    [Required]
    public DateTime StartDateUtc { get; set; }

    [Required]
    public DateTime EndDateUtc { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateRentalItem> Items { get; set; } = new();

    // Typ wynajmu (godzinowy/dzienny)
    public RentalTypeDto RentalType { get; set; } = RentalTypeDto.Daily;
    public int? HoursRented { get; set; }

    /// <summary>
    /// Docelowy kontrakt marketplace: jedna grupa na wypożyczalnię, każda z
    /// własnym terminem. Gdy lista jest pusta, API obsługuje starszy płaski
    /// kontrakt powyżej dla kompatybilności wdrożeń w toku.
    /// </summary>
    public List<RentalGroupQuoteRequest> RentalGroups { get; set; } = new();
}

public class RentalGroupQuoteRequest
{
    [Required]
    public Guid TenantId { get; set; }

    [Required]
    public DateTime StartDateUtc { get; set; }

    [Required]
    public DateTime EndDateUtc { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateRentalItem> Items { get; set; } = new();

    public RentalTypeDto RentalType { get; set; } = RentalTypeDto.Daily;
    public int? HoursRented { get; set; }
}

public class PaymentQuoteResponse
{
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public string Currency { get; set; } = "PLN";
    public int RentalDays { get; set; }
    public int RentalCount { get; set; }
    public List<TenantQuoteBreakdown> Tenants { get; set; } = new();
    public List<PaymentQuoteItemBreakdown> Items { get; set; } = new();
}

public class PaymentQuoteItemBreakdown
{
    public Guid ProductId { get; set; }
    public decimal Subtotal { get; set; }
}

public class TenantQuoteBreakdown
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string? PickupAddress { get; set; }
    public string? PickupCity { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? OpeningHours { get; set; }
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    public RentalTypeDto RentalType { get; set; } = RentalTypeDto.Daily;
    public int? HoursRented { get; set; }
    public int RentalDays { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public RentalTermsSummary RentalTerms { get; set; } = new();
}

public class RentalTermsSummary
{
    public string Title { get; set; } = "Standardowy regulamin wypożyczalni RentSpot";
    public string Version { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string? Content { get; set; }
    public bool UsesPlatformDefault { get; set; } = true;
}

public class CreatePaymentIntentRequest
{
    [Required]
    public DateTime StartDateUtc { get; set; }

    [Required]
    public DateTime EndDateUtc { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateRentalItem> Items { get; set; } = new();

    public string Currency { get; set; } = "PLN";
}

public class PaymentIntentDto
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal DepositAmount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string Status { get; set; } = PaymentIntentStatus.Succeeded;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string? ClientSecret { get; set; } // For Stripe.js frontend integration
}

public static class PaymentIntentStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string RequiresPaymentMethod = "RequiresPaymentMethod";
    public const string RequiresConfirmation = "RequiresConfirmation";
    public const string RequiresAction = "RequiresAction";
    public const string RequiresCapture = "RequiresCapture";
    public const string Succeeded = "Succeeded";
    public const string Canceled = "Canceled";
    public const string Failed = "Failed";
    
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Pending,
        Processing,
        RequiresPaymentMethod,
        RequiresConfirmation,
        RequiresAction,
        RequiresCapture,
        Succeeded,
        Canceled,
        Failed
    };
}
