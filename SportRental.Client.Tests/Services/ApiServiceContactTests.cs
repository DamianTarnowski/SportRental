using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SportRental.Shared.Models;
using SportRental.Shared.Services;

namespace SportRental.Client.Tests.Services;

public class ApiServiceContactTests
{
    [Fact]
    public async Task SendContactMessageAsync_PostsRequestToContactEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new DelegateHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var service = new ApiService(new HttpClient(handler));
        service.SetBaseUrl("https://api.example.test/");
        var payload = ValidRequest();

        await service.SendContactMessageAsync(payload);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri.Should().Be("https://api.example.test/api/contact");
        using var json = JsonDocument.Parse(capturedBody!);
        json.RootElement.GetProperty("tenantId").GetGuid().Should().Be(payload.TenantId);
        json.RootElement.GetProperty("email").GetString().Should().Be(payload.Email);
        json.RootElement.GetProperty("message").GetString().Should().Be(payload.Message);
    }

    [Fact]
    public async Task SendContactMessageAsync_PropagatesSafeApiErrorAndStatus()
    {
        var handler = new DelegateHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(
                "{\"error\":\"Wypożyczalnia nie obsługuje wiadomości.\"}",
                Encoding.UTF8,
                "application/json")
        }));
        var service = new ApiService(new HttpClient(handler));
        service.SetBaseUrl("https://api.example.test");

        var action = () => service.SendContactMessageAsync(ValidRequest());

        var exception = await action.Should().ThrowAsync<HttpRequestException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        exception.Which.Message.Should().Be("Wypożyczalnia nie obsługuje wiadomości.");
    }

    private static ContactMessageRequest ValidRequest() => new()
    {
        TenantId = Guid.NewGuid(),
        Name = "Jan Kowalski",
        Email = "jan@example.com",
        Phone = "+48 123 456 789",
        Subject = "Pytanie o sprzęt",
        Message = "Czy wybrany sprzęt jest dostępny w sobotę?"
    };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handle(request);
    }
}
