using System.Net;
using System.Text;
using FluentAssertions;
using SportRental.Shared.Services;

namespace SportRental.Client.Tests.Services;

public class ApiServiceRentalTests
{
    [Fact]
    public async Task CancelRentalAsync_ReturnsTrueAfterSuccessfulDelete()
    {
        HttpRequestMessage? capturedRequest = null;
        var service = CreateService(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var rentalId = Guid.NewGuid();

        var result = await service.CancelRentalAsync(rentalId);

        result.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Delete);
        capturedRequest.RequestUri.Should().Be($"https://api.example.test/api/rentals/{rentalId}");
    }

    [Fact]
    public async Task CancelRentalAsync_PropagatesSafeApiError()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                "{\"error\":\"Wpłaconego wynajmu nie można anulować automatycznie.\"}",
                Encoding.UTF8,
                "application/json")
        });

        var action = () => service.CancelRentalAsync(Guid.NewGuid());

        var exception = await action.Should().ThrowAsync<HttpRequestException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
        exception.Which.Message.Should().Be("Wpłaconego wynajmu nie można anulować automatycznie.");
    }

    private static ApiService CreateService(Func<HttpRequestMessage, HttpResponseMessage> handle)
    {
        var service = new ApiService(new HttpClient(new DelegateHandler(handle)));
        service.SetBaseUrl("https://api.example.test");
        return service;
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(handle(request));
    }
}
