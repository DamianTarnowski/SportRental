using System.Net;
using System.Text;
using FluentAssertions;
using SportRental.Shared.Legal;
using SportRental.Shared.Services;

namespace SportRental.Client.Tests.Services;

public class ApiServiceLegalTests
{
    [Fact]
    public async Task GetLegalInfoAsync_ReadsPublicOperatorDataAndVersions()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new DelegateHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    {
                      "serviceName": "RentSpot",
                      "operatorName": "Operator testowy",
                      "termsVersion": "{{LegalDocumentVersions.Terms}}",
                      "privacyVersion": "{{LegalDocumentVersions.Privacy}}",
                      "effectiveFromUtc": "{{LegalDocumentVersions.EffectiveFromUtc:O}}",
                      "isOperatorDataComplete": true
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        });
        var api = new ApiService(new HttpClient(handler));
        api.SetBaseUrl("https://api.example.test/");

        var result = await api.GetLegalInfoAsync();

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Get);
        capturedRequest.RequestUri.Should().Be("https://api.example.test/api/legal/info");
        result.OperatorName.Should().Be("Operator testowy");
        result.TermsVersion.Should().Be(LegalDocumentVersions.Terms);
        result.PrivacyVersion.Should().Be(LegalDocumentVersions.Privacy);
        result.IsOperatorDataComplete.Should().BeTrue();
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handle(request);
    }
}
