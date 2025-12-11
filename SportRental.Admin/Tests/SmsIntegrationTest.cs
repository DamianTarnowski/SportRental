using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportRental.Admin.Services.Sms;

namespace SportRental.Admin.Tests;

/// <summary>
/// Prosty test integracyjny do wysyłania SMS przez SerwerSMS.pl
/// Uruchom: dotnet run --project SportRental.Admin -- --test-sms 667362375
/// </summary>
public class SmsIntegrationTest
{
    public static async Task RunAsync(string phoneNumber)
    {
        Console.WriteLine("=== Test integracyjny SMS (SerwerSMS.pl) ===");
        Console.WriteLine($"Numer docelowy: {phoneNumber}");
        Console.WriteLine();

        // Konfiguracja z appsettings.json
        var settings = new SerwerSmsSettings
        {
            IsEnabled = true,
            Username = "webapi_sportrental",
            Password = "0d^9v&S1eATr.oaNTTe&",
            SenderName = "SportRental",
            UseSmsEco = true,
            MaxRetries = 1,
            TestMode = false
        };

        Console.WriteLine("Konfiguracja:");
        Console.WriteLine($"  Username: {settings.Username}");
        Console.WriteLine($"  IsEnabled: {settings.IsEnabled}");
        Console.WriteLine($"  UseSmsEco: {settings.UseSmsEco}");
        Console.WriteLine($"  SenderName: {settings.SenderName}");
        Console.WriteLine();

        // Utwórz HttpClient
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api2.serwersms.pl/")
        };

        // Logger do konsoli
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<SerwerSmsSender>();

        // Utwórz sender
        var httpClientFactory = new SimpleHttpClientFactory(httpClient);
        var smsSender = new SerwerSmsSender(Options.Create(settings), logger, httpClientFactory);

        var message = $"Test SMS z SportRental - {DateTime.Now:HH:mm:ss}";
        Console.WriteLine($"Treść wiadomości: {message}");
        Console.WriteLine();

        Console.WriteLine("Wysyłanie SMS...");
        try
        {
            await smsSender.SendAsync(phoneNumber, message);
            Console.WriteLine();
            Console.WriteLine("✅ SMS wysłany pomyślnie!");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"❌ Błąd wysyłania SMS:");
            Console.WriteLine($"   Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
            Console.WriteLine();
            Console.WriteLine("Szczegóły:");
            Console.WriteLine(ex.ToString());
        }
    }
}

/// <summary>
/// Prosta implementacja IHttpClientFactory dla testu
/// </summary>
public class SimpleHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _httpClient;

    public SimpleHttpClientFactory(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public HttpClient CreateClient(string name)
    {
        return _httpClient;
    }
}
