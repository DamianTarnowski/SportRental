using Microsoft.AspNetCore.Http;
using SportRental.Admin.Routing;

namespace SportRental.Admin.Tests.Routing;

public sealed class ClientRouteRedirectsTests
{
    [Theory]
    [InlineData("/products", "", "/_client/products")]
    [InlineData("/products/11111111-1111-1111-1111-111111111111", "?tenantId=abc&page=2", "/_client/products/11111111-1111-1111-1111-111111111111?tenantId=abc&page=2")]
    [InlineData("/checkout/cancel", "?session_id=cs_test", "/_client/checkout/cancel?session_id=cs_test")]
    [InlineData("/my-rentals/22222222-2222-2222-2222-222222222222", "", "/_client/my-rentals/22222222-2222-2222-2222-222222222222")]
    public void BuildBundledClientLocation_PreservesPathAndQueryWithinClientPrefix(
        string path,
        string query,
        string expected)
    {
        var result = ClientRouteRedirects.BuildBundledClientLocation(
            new PathString(path),
            new QueryString(query));

        Assert.Equal(expected, result);
        Assert.StartsWith("/_client/", result, StringComparison.Ordinal);
    }
}
