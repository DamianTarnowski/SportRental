using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Godziny pracy wypożyczalni — tygodniowy plan per tenant.
/// Faza 8a (Bookero parity): blokowanie rezerwacji poza godzinami.
/// Jeśli tenant nie ma własnego schedule, fallback w service: 8:00–20:00 codziennie.
/// </summary>
public class BusinessHoursSchedule
{
    [Key]
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<BusinessHoursDay> Days { get; set; } = new List<BusinessHoursDay>();

    public Tenant? Tenant { get; set; }
}

public class BusinessHoursDay
{
    [Key]
    public Guid Id { get; set; }
    public Guid ScheduleId { get; set; }

    /// <summary>0=Sunday … 6=Saturday (System.DayOfWeek).</summary>
    public DayOfWeek DayOfWeek { get; set; }

    public bool IsClosed { get; set; }

    /// <summary>Godzina otwarcia (lokalna Europe/Warsaw). Null jeśli IsClosed.</summary>
    public TimeOnly? OpenFrom { get; set; }

    /// <summary>Godzina zamknięcia. Null jeśli IsClosed.</summary>
    public TimeOnly? OpenTo { get; set; }

    public BusinessHoursSchedule? Schedule { get; set; }
}

/// <summary>
/// Wyjątki w grafiku — dni wolne (święta, inwentaryzacja) albo zmiana godzin (skrócony dzień).
/// </summary>
public class BusinessHoursException
{
    [Key]
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public DateOnly Date { get; set; }
    public bool IsClosed { get; set; }

    public TimeOnly? CustomOpen { get; set; }
    public TimeOnly? CustomClose { get; set; }

    [MaxLength(256)]
    public string? Reason { get; set; }

    public Tenant? Tenant { get; set; }
}
