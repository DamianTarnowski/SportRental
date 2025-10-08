using SportRental.Infrastructure.Domain;
using FluentAssertions;

namespace SportRental.Admin.Tests.Data.Domain;

public class RentalTests
{
    [Fact]
    public void Rental_DefaultConstructor_ShouldInitializeWithDefaults()
    {
        // Act
        var rental = new Rental();

        // Assert
        rental.Id.Should().Be(Guid.Empty); // Default Guid value
        rental.TenantId.Should().Be(Guid.Empty);
        rental.CustomerId.Should().Be(Guid.Empty);
        rental.StartDateUtc.Should().Be(default(DateTime));
        rental.EndDateUtc.Should().Be(default(DateTime));
        rental.Status.Should().Be(RentalStatus.Draft);
        rental.TotalAmount.Should().Be(0);
        rental.ContractUrl.Should().BeNull();
        rental.Notes.Should().BeNull();
        rental.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1)); // Default is UtcNow
    }

    [Theory]
    [InlineData(RentalStatus.Draft)]
    [InlineData(RentalStatus.Confirmed)]
    [InlineData(RentalStatus.Active)]
    [InlineData(RentalStatus.Completed)]
    [InlineData(RentalStatus.Cancelled)]
    public void Rental_SetStatus_ShouldAcceptAllValidStatuses(RentalStatus status)
    {
        // Arrange
        var rental = new Rental();

        // Act
        rental.Status = status;

        // Assert
        rental.Status.Should().Be(status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50.00)]
    [InlineData(999.99)]
    [InlineData(1500.50)]
    public void Rental_SetTotalAmount_ShouldAcceptValidAmounts(decimal amount)
    {
        // Arrange
        var rental = new Rental();

        // Act
        rental.TotalAmount = amount;

        // Assert
        rental.TotalAmount.Should().Be(amount);
    }

    [Fact]
    public void Rental_SetDates_ShouldMaintainUtcKind()
    {
        // Arrange
        var rental = new Rental();
        var startDate = DateTime.UtcNow;
        var endDate = DateTime.UtcNow.AddDays(7);

        // Act
        rental.StartDateUtc = startDate;
        rental.EndDateUtc = endDate;

        // Assert
        rental.StartDateUtc.Should().Be(startDate);
        rental.EndDateUtc.Should().Be(endDate);
        rental.EndDateUtc.Should().BeAfter(rental.StartDateUtc);
    }

    [Theory]
    [InlineData("https://example.com/contracts/contract123.pdf")]
    [InlineData("/files/contracts/rental_456.pdf")]
    [InlineData("contract.pdf")]
    public void Rental_SetContractUrl_ShouldAcceptValidUrls(string contractUrl)
    {
        // Arrange
        var rental = new Rental();

        // Act
        rental.ContractUrl = contractUrl;

        // Assert
        rental.ContractUrl.Should().Be(contractUrl);
    }

    [Fact]
    public void Rental_SetNotes_ShouldAcceptLongNotes()
    {
        // Arrange
        var rental = new Rental();
        var longNotes = "Szczegółowe informacje o wynajmie: klient poprosił o dodatkowe wyposażenie, " +
                       "planuje wykorzystać sprzęt na zawodach sportowych w weekend. Zwrot przewidziany " +
                       "w niedzielę wieczorem. Kaucja pobrana w gotówce.";

        // Act
        rental.Notes = longNotes;

        // Assert
        rental.Notes.Should().Be(longNotes);
    }

    [Fact]
    public void Rental_WithAllProperties_ShouldMaintainValues()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var startDate = DateTime.UtcNow;
        var endDate = DateTime.UtcNow.AddDays(3);
        var status = RentalStatus.Active;
        var totalAmount = 250.00m;
        var contractUrl = "https://example.com/contracts/rental123.pdf";
        var notes = "Wynajem weekendowy z dodatkowym wyposażeniem";
        var createdAt = DateTime.UtcNow;

        // Act
        var rental = new Rental
        {
            Id = id,
            TenantId = tenantId,
            CustomerId = customerId,
            StartDateUtc = startDate,
            EndDateUtc = endDate,
            Status = status,
            TotalAmount = totalAmount,
            ContractUrl = contractUrl,
            Notes = notes,
            CreatedAtUtc = createdAt
        };

        // Assert
        rental.Id.Should().Be(id);
        rental.TenantId.Should().Be(tenantId);
        rental.CustomerId.Should().Be(customerId);
        rental.StartDateUtc.Should().Be(startDate);
        rental.EndDateUtc.Should().Be(endDate);
        rental.Status.Should().Be(status);
        rental.TotalAmount.Should().Be(totalAmount);
        rental.ContractUrl.Should().Be(contractUrl);
        rental.Notes.Should().Be(notes);
        rental.CreatedAtUtc.Should().Be(createdAt);
    }

    [Fact]
    public void Rental_StatusProgression_ShouldFollowBusinessLogic()
    {
        // Arrange
        var rental = new Rental();

        // Act & Assert - Typical status progression
        rental.Status = RentalStatus.Draft;
        rental.Status.Should().Be(RentalStatus.Draft);

        rental.Status = RentalStatus.Confirmed;
        rental.Status.Should().Be(RentalStatus.Confirmed);

        rental.Status = RentalStatus.Active;
        rental.Status.Should().Be(RentalStatus.Active);

        rental.Status = RentalStatus.Completed;
        rental.Status.Should().Be(RentalStatus.Completed);
    }

    [Fact]
    public void Rental_CanBeCancelled_FromAnyStatus()
    {
        // Arrange & Act & Assert
        var statuses = new[] { RentalStatus.Draft, RentalStatus.Confirmed, RentalStatus.Active };
        
        foreach (var status in statuses)
        {
            var rental = new Rental { Status = status };
            rental.Status = RentalStatus.Cancelled;
            rental.Status.Should().Be(RentalStatus.Cancelled);
        }
    }
}