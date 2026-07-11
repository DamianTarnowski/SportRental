using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SportRental.Shared.Services;

namespace SportRental.Client.Tests.Services;

public sealed class ApiServiceGuestAccessTests
{
    [Fact]
    public async Task RequestGuestOrderAccessAsync_PostsEmailAndOrderNumber()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new DelegateHandler(async request =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var api = new ApiService(new HttpClient(handler));
        api.SetBaseUrl("https://api.example.test/");

        var result = await api.RequestGuestOrderAccessAsync(new GuestOrderAccessRequest
        {
            Email = "guest@example.test",
            OrderNumber = "RS-20260710-ABC12345"
        });

        result.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri.Should().Be(
            "https://api.example.test/api/auth/guest-order-access/request");
        using var json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("email").GetString().Should().Be("guest@example.test");
        json.RootElement.GetProperty("orderNumber").GetString().Should().Be("RS-20260710-ABC12345");
    }

    [Fact]
    public async Task RedeemGuestOrderAccessAsync_ReturnsRecoveredGuestSession()
    {
        var customerId = Guid.NewGuid();
        var handler = new DelegateHandler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "expiresIn": 3600,
                  "customerId": "{{customerId}}",
                  "email": "guest@example.test",
                  "fullName": "Jan Gość"
                }
                """,
                Encoding.UTF8,
                "application/json")
        }));
        var api = new ApiService(new HttpClient(handler));
        api.SetBaseUrl("https://api.example.test");

        var result = await api.RedeemGuestOrderAccessAsync("jednorazowy-token");

        result.Should().NotBeNull();
        result!.CustomerId.Should().Be(customerId);
        result.Email.Should().Be("guest@example.test");
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handle(request);
    }
}
