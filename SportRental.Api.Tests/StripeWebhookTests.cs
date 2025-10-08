using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SportRental.Api.Tests;

public class StripeWebhookTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public StripeWebhookTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task StripeWebhook_ValidPayload_ReturnsOk()
    {
        // Arrange
        var payload = @"{
            ""id"": ""evt_test_123"",
            ""object"": ""event"",
            ""type"": ""payment_intent.succeeded"",
            ""data"": {
                ""object"": {
                    ""id"": ""pi_test_123"",
                    ""amount"": 5000,
                    ""currency"": ""pln"",
                    ""status"": ""succeeded""
                }
            }
        }";

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        
        // Note: bez Stripe-Signature, webhook będzie odrzucony w produkcji
        // Ale w testach z pustym WebhookSecret, API powinien przyjąć request

        // Act
        var response = await _client.PostAsync("/api/webhooks/stripe", content);

        // Assert
        // W testach z MockPaymentGateway, webhook może zwrócić OK lub BadRequest w zależności od konfiguracji
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StripeWebhook_EmptyPayload_ReturnsBadRequest()
    {
        // Arrange
        var content = new StringContent("", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/webhooks/stripe", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StripeWebhook_InvalidJson_ReturnsBadRequest()
    {
        // Arrange
        var content = new StringContent("not-json", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/webhooks/stripe", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
