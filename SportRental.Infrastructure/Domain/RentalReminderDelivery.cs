namespace SportRental.Infrastructure.Domain;

public enum RentalReminderStage
{
    Final = 1,
    OverdueDay1 = 2,
    OverdueDay3 = 3
}

public enum RentalReminderChannel
{
    Email = 1,
    Sms = 2
}

/// <summary>
/// Rejestr skutecznie dostarczonych przypomnień. Osobny rekord na etap i kanał
/// pozwala ponowić SMS, gdy e-mail przeszedł, ale SMS nie został wysłany.
/// </summary>
public class RentalReminderDelivery
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RentalId { get; set; }
    public RentalReminderStage Stage { get; set; }
    public RentalReminderChannel Channel { get; set; }
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

    public Rental? Rental { get; set; }
}
