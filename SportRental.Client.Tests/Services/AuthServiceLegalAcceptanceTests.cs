using System.Net;
using System.Text;
using System.Text.Json;
using Blazored.LocalStorage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using SportRental.Client.Services;
using SportRental.Shared.Legal;

namespace SportRental.Client.Tests.Services;

public class AuthServiceLegalAcceptanceTests
{
    [Fact]
    public async Task RegisterAsync_SendsCurrentLegalDocumentVersions()
    {
        string? requestBody = null;
        var handler = new DelegateHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "expiresIn": 0,
                      "user": {
                        "id": "11111111-1111-1111-1111-111111111111",
                        "email": "jan@example.test",
                        "customerId": "22222222-2222-2222-2222-222222222222"
                      },
                      "emailConfirmationRequired": true
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "https://api.example.test"
            })
            .Build();
        var authenticationStateProvider = new ApiAuthenticationStateProvider(
            httpClient,
            Mock.Of<ILocalStorageService>(),
            configuration);
        var service = new AuthService(
            httpClient,
            authenticationStateProvider,
            Mock.Of<ICustomerSessionService>(),
            configuration);

        var result = await service.RegisterAsync(
            "jan@example.test",
            "StableClient123",
            "Jan Kowalski");

        result.Succeeded.Should().BeTrue();
        result.EmailConfirmationRequired.Should().BeTrue();
        using var json = JsonDocument.Parse(requestBody!);
        json.RootElement.GetProperty("acceptedTermsVersion").GetString()
            .Should().Be(LegalDocumentVersions.Terms);
        json.RootElement.GetProperty("acknowledgedPrivacyVersion").GetString()
            .Should().Be(LegalDocumentVersions.Privacy);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handle(request);
    }
}
