using System.Net.Http.Headers;

namespace SportRental.Api.Tests;

internal static class TestApiClientHelper
{
    internal static void AuthenticateClient(HttpClient client, Guid tenantId)
    {
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Remove(TestAuthHandler.CustomerIdHeader);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.RoleHeader);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.EmailHeader);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthHandler.SchemeName);
    }

    internal static void AuthenticateAsCustomer(HttpClient client, Guid tenantId, Guid customerId, string? email = null)
    {
        AuthenticateClient(client, tenantId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.CustomerIdHeader, customerId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }
    }

    internal static void AuthenticateAsAdmin(HttpClient client, Guid tenantId)
    {
        AuthenticateClient(client, tenantId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Admin");
    }
}
