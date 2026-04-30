using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SportRental.Admin.Services.Sms;

namespace SportRental.Admin.Tests.Services.Sms;

[Trait("Category", "RequiresLiveServices")]
public class SmsApiSenderIntegrationTests
{
    [Fact]
    public async Task SendAsync_ToRealNumber_ShouldDeliverSms()
    {
        // Konfiguracja z appsettings.json w SportRental.Admin
        var config = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "SportRental.Admin"))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var smsApiSection = config.GetSection("smsApi");
        var settings = new SmsApiSettings
        {
            AuthToken = smsApiSection["authToken"] ?? "",
            IsEnabled = smsApiSection.GetValue<bool>("isEnabled"),
            SendConfirmationAttempts = smsApiSection.GetValue<int?>("sendConfirmationAttempts") ?? 3,
            SenderName = smsApiSection["senderName"] ?? "Test"
        };

        if (!settings.IsEnabled || string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            Assert.Fail("smsApi not enabled or authToken missing in appsettings.json");
            return;
        }

        var logger = new Mock<ILogger<SmsApiSender>>().Object;
        var sender = new SmsApiSender(Options.Create(settings), logger);

        // Twój numer testowy
        var phoneNumber = "667362375";
        var message = $"SportRental SMSAPI test - {DateTime.Now:HH:mm:ss}";

        var act = async () => await sender.SendAsync(phoneNumber, message);
        await act.Should().NotThrowAsync();
    }
}
