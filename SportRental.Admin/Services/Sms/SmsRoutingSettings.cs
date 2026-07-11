namespace SportRental.Admin.Services.Sms;

public class SmsRoutingSettings
{
    public const string SectionName = "Sms";
    public const string LegacyReplyConfirmationEnabledKey = SectionName + ":LegacyReplyConfirmationEnabled";

    public string Provider { get; set; } = "SerwerSms";
    public bool LegacyReplyConfirmationEnabled { get; set; }
}
