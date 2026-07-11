using System.Net;
using System.Text;
using FluentAssertions;
using SportRental.Shared.Models;
using SportRental.Shared.Services;

namespace SportRental.Client.Tests.Services;

public class ApiServiceHoldTests
{
    [Fact]
    public async Task CreateHoldAsync_ExposesSafeBusinessErrorForClientFeedback()
    {
        var handler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":\"Wypożyczalnia jest zamknięta o godzinie odbioru.\"}",
                Encoding.UTF8,
                "application/json")
        });
        var api = new ApiService(new HttpClient(handler));
        api.SetBaseUrl("https://api.example.test");

        var result = await api.CreateHoldAsync(new CreateHoldRequest
        {
            ProductId = Guid.NewGuid(),
            Quantity = 1,
            StartDateUtc = DateTime.UtcNow.AddDays(1),
            EndDateUtc = DateTime.UtcNow.AddDays(2)
        });

        result.Should().BeNull();
        api.LastHoldError.Should().Be("Wypożyczalnia jest zamknięta o godzinie odbioru.");
    }

    [Fact]
    public async Task CreateHoldAsync_DoesNotExposeRawHtmlError()
    {
        var handler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>proxy details</html>")
        });
        var api = new ApiService(new HttpClient(handler));
        api.SetBaseUrl("https://api.example.test");

        await api.CreateHoldAsync(new CreateHoldRequest
        {
            ProductId = Guid.NewGuid(),
            Quantity = 1,
            StartDateUtc = DateTime.UtcNow.AddDays(1),
            EndDateUtc = DateTime.UtcNow.AddDays(2)
        });

        api.LastHoldError.Should().Be("Nie udało się zarezerwować produktu dla wybranego terminu.");
        api.LastHoldError.Should().NotContain("proxy details");
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
