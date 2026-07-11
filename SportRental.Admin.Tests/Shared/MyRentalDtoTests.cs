using FluentAssertions;
using SportRental.Shared.Models;

namespace SportRental.Admin.Tests.Shared;

public class MyRentalDtoTests
{
    [Fact]
    public void PublicContractUrl_ShouldUseServerGeneratedValue()
    {
        var rental = new MyRentalDto
        {
            Id = Guid.Parse("1954eb65-1111-2222-3333-444444444444"),
            ContractUrl = "contracts/tenant/umowa.pdf",
            PublicContractUrl = "/c/protected-token"
        };

        rental.PublicContractUrl.Should().Be("/c/protected-token");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PublicContractUrl_DefaultsToNull(string? contractUrl)
    {
        var rental = new MyRentalDto
        {
            Id = Guid.Parse("1954eb65-1111-2222-3333-444444444444"),
            ContractUrl = contractUrl
        };

        rental.PublicContractUrl.Should().BeNull();
    }
}
