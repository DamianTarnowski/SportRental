using FluentAssertions;
using SportRental.Shared.Models;

namespace SportRental.Client.Tests.Models;

public sealed class ProductFilterRequestTests
{
    [Fact]
    public void ToQueryString_UsesTenantIdInsteadOfTenantDisplayName()
    {
        var tenantId = Guid.NewGuid();
        var request = new ProductFilterRequest
        {
            Page = 2,
            PageSize = 12,
            TenantId = tenantId
        };

        var query = request.ToQueryString();

        query.Should().Contain($"tenantId={tenantId:D}");
        query.Should().NotContain("tenant=");
    }
}
