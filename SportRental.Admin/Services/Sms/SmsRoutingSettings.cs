namespace SportRental.Admin.Services.Sms;

public class SmsRoutingSettings
{
    public const string SectionName = "Sms";
    public string Provider { get; set; } = "SerwerSms";
}
