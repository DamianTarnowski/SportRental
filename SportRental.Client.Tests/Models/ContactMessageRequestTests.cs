using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using SportRental.Shared.Models;

namespace SportRental.Client.Tests.Models;

public class ContactMessageRequestTests
{
    [Fact]
    public void Validation_RejectsEmptyTenantAndMalformedFields()
    {
        var request = new ContactMessageRequest
        {
            TenantId = Guid.Empty,
            Name = "A",
            Email = "nie-email",
            Phone = new string('1', 31),
            Subject = "x",
            Message = "za krótka"
        };

        var results = Validate(request);

        results.Should().Contain(result => result.MemberNames.Contains(nameof(request.TenantId)));
        results.Should().Contain(result => result.MemberNames.Contains(nameof(request.Name)));
        results.Should().Contain(result => result.MemberNames.Contains(nameof(request.Email)));
        results.Should().Contain(result => result.MemberNames.Contains(nameof(request.Phone)));
        results.Should().Contain(result => result.MemberNames.Contains(nameof(request.Subject)));
        results.Should().Contain(result => result.MemberNames.Contains(nameof(request.Message)));
    }

    [Fact]
    public void Validation_AcceptsCompleteMessage()
    {
        var request = new ContactMessageRequest
        {
            TenantId = Guid.NewGuid(),
            Name = "Jan Kowalski",
            Email = "jan@example.com",
            Phone = "+48 123 456 789",
            Subject = "Pytanie o rezerwację",
            Message = "Proszę o informację, czy sprzęt jest dostępny."
        };

        Validate(request).Should().BeEmpty();
    }

    private static List<ValidationResult> Validate(ContactMessageRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);
        return results;
    }
}
