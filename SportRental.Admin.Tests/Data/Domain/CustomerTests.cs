using SportRental.Infrastructure.Domain;
using FluentAssertions;

namespace SportRental.Admin.Tests.Data.Domain;

public class CustomerTests
{
    [Fact]
    public void Customer_DefaultConstructor_ShouldInitializeWithDefaults()
    {
        // Act
        var customer = new Customer();

        // Assert
        customer.Id.Should().Be(Guid.Empty); // Default Guid value
        customer.TenantId.Should().Be(Guid.Empty);
        customer.FullName.Should().Be(string.Empty); // Has default value
        customer.Email.Should().BeNull();
        customer.PhoneNumber.Should().BeNull();
        customer.Address.Should().BeNull();
        customer.Notes.Should().BeNull();
        customer.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("Jan Kowalski")]
    [InlineData("Anna Maria Nowak-Kowalska")]
    [InlineData("José María García")]
    public void Customer_SetFullName_ShouldAcceptValidNames(string fullName)
    {
        // Arrange
        var customer = new Customer();

        // Act
        customer.FullName = fullName;

        // Assert
        customer.FullName.Should().Be(fullName);
    }

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name+tag@example.co.uk")]
    [InlineData("firstname.lastname@company.org")]
    public void Customer_SetEmail_ShouldAcceptValidEmails(string email)
    {
        // Arrange
        var customer = new Customer();

        // Act
        customer.Email = email;

        // Assert
        customer.Email.Should().Be(email);
    }

    [Theory]
    [InlineData("+48123456789")]
    [InlineData("+1234567890")]
    [InlineData("123-456-789")]
    public void Customer_SetPhoneNumber_ShouldAcceptValidPhoneNumbers(string phoneNumber)
    {
        // Arrange
        var customer = new Customer();

        // Act
        customer.PhoneNumber = phoneNumber;

        // Assert
        customer.PhoneNumber.Should().Be(phoneNumber);
    }

    [Fact]
    public void Customer_SetAddress_ShouldAcceptLongAddress()
    {
        // Arrange
        var customer = new Customer();
        var longAddress = "ul. Długa Nazwa Ulicy z Wieloma Słowami 123/45, 00-001 Warszawa, Województwo Mazowieckie, Polska";

        // Act
        customer.Address = longAddress;

        // Assert
        customer.Address.Should().Be(longAddress);
        customer.Address!.Length.Should().BeLessThanOrEqualTo(512); // Based on MaxLength attribute
    }

    [Fact]
    public void Customer_SetNotes_ShouldAcceptLongNotes()
    {
        // Arrange
        var customer = new Customer();
        var longNotes = string.Concat(Enumerable.Repeat("Test note content. ", 50)); // About 900 characters

        // Act
        customer.Notes = longNotes;

        // Assert
        customer.Notes.Should().Be(longNotes);
        customer.Notes!.Length.Should().BeLessThanOrEqualTo(1024); // Based on MaxLength attribute
    }

    [Fact]
    public void Customer_WithAllProperties_ShouldMaintainValues()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var fullName = "Jan Kowalski";
        var email = "jan.kowalski@example.com";
        var phoneNumber = "+48123456789";
        var address = "ul. Testowa 123, 00-001 Warszawa";
        var notes = "Klient regularny, preferuje wypożyczenia weekendowe";

        // Act
        var customer = new Customer
        {
            Id = id,
            TenantId = tenantId,
            FullName = fullName,
            Email = email,
            PhoneNumber = phoneNumber,
            Address = address,
            Notes = notes
        };

        // Assert
        customer.Id.Should().Be(id);
        customer.TenantId.Should().Be(tenantId);
        customer.FullName.Should().Be(fullName);
        customer.Email.Should().Be(email);
        customer.PhoneNumber.Should().Be(phoneNumber);
        customer.Address.Should().Be(address);
        customer.Notes.Should().Be(notes);
    }
}