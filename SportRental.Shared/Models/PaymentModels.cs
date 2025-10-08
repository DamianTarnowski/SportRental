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
}

public class PaymentQuoteResponse
{
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public string Currency { get; set; } = "PLN";
    public int RentalDays { get; set; }
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
    public Guid Id { get; set; }
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
