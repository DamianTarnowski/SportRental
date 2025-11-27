using System.Net.Http.Headers;

namespace SportRental.Api.Tests;

internal static class TestApiClientHelper
{
    internal static void AuthenticateClient(HttpClient client, Guid tenantId)
    {
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName);
    }
}
