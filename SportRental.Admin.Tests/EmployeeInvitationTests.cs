using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using Xunit;

namespace SportRental.Admin.Tests;

/// <summary>
/// Testy systemu zaproszeń pracowników
/// </summary>
public class EmployeeInvitationTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateEmployeeInvitation_ShouldSaveToDatabase()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        
        var invitation = new EmployeeInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = "test.employee@example.com",
            FullName = "Jan Kowalski",
            Role = EmployeeRole.Pracownik,
            Token = GenerateToken(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            IsUsed = false
        };

        // Act
        context.EmployeeInvitations.Add(invitation);
        await context.SaveChangesAsync();

        // Assert
        var saved = await context.EmployeeInvitations.FirstOrDefaultAsync(i => i.Id == invitation.Id);
        saved.Should().NotBeNull();
        saved!.Email.Should().Be("test.employee@example.com");
        saved.FullName.Should().Be("Jan Kowalski");
        saved.Role.Should().Be(EmployeeRole.Pracownik);
        saved.TenantId.Should().Be(tenantId);
        saved.IsUsed.Should().BeFalse();
    }

    [Fact]
    public async Task FindInvitationByToken_ShouldReturnCorrectInvitation()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        var token = GenerateToken();
        
        var invitation = new EmployeeInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = "worker@test.com",
            Role = EmployeeRole.Kierownik,
            Token = token,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            IsUsed = false
        };

        context.EmployeeInvitations.Add(invitation);
        await context.SaveChangesAsync();

        // Act
        var found = await context.EmployeeInvitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Token == token);

        // Assert
        found.Should().NotBeNull();
        found!.Email.Should().Be("worker@test.com");
        found.Role.Should().Be(EmployeeRole.Kierownik);
    }

    [Fact]
    public async Task MarkInvitationAsUsed_ShouldUpdateCorrectly()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        
        var invitation = new EmployeeInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = "new.employee@test.com",
            Role = EmployeeRole.Manager,
            Token = GenerateToken(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            IsUsed = false
        };

        context.EmployeeInvitations.Add(invitation);
        await context.SaveChangesAsync();

        // Act - mark as used
        invitation.IsUsed = true;
        invitation.UsedAtUtc = DateTime.UtcNow;
        invitation.CreatedEmployeeId = employeeId;
        await context.SaveChangesAsync();

        // Assert
        var updated = await context.EmployeeInvitations.FindAsync(invitation.Id);
        updated.Should().NotBeNull();
        updated!.IsUsed.Should().BeTrue();
        updated.UsedAtUtc.Should().NotBeNull();
        updated.CreatedEmployeeId.Should().Be(employeeId);
    }

    [Fact]
    public async Task ExpiredInvitation_ShouldBeDetectable()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        
        var expiredInvitation = new EmployeeInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = "expired@test.com",
            Role = EmployeeRole.Pracownik,
            Token = GenerateToken(),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-3), // Expired 3 days ago
            IsUsed = false
        };

        context.EmployeeInvitations.Add(expiredInvitation);
        await context.SaveChangesAsync();

        // Act
        var invitation = await context.EmployeeInvitations.FindAsync(expiredInvitation.Id);
        var isExpired = invitation!.ExpiresAtUtc < DateTime.UtcNow;

        // Assert
        isExpired.Should().BeTrue();
    }

    [Fact]
    public async Task ValidInvitation_ShouldNotBeExpired()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        
        var validInvitation = new EmployeeInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = "valid@test.com",
            Role = EmployeeRole.Pracownik,
            Token = GenerateToken(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7), // Valid for 7 more days
            IsUsed = false
        };

        context.EmployeeInvitations.Add(validInvitation);
        await context.SaveChangesAsync();

        // Act
        var invitation = await context.EmployeeInvitations.FindAsync(validInvitation.Id);
        var isValid = !invitation!.IsUsed && invitation.ExpiresAtUtc > DateTime.UtcNow;

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task TenantFilter_ShouldFilterInvitationsByTenant()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var tenant1Id = Guid.NewGuid();
        var tenant2Id = Guid.NewGuid();
        
        var invitation1 = new EmployeeInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenant1Id,
            Email = "tenant1@test.com",
            Role = EmployeeRole.Pracownik,
            Token = GenerateToken(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            IsUsed = false
        };

        var invitation2 = new EmployeeInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenant2Id,
            Email = "tenant2@test.com",
            Role = EmployeeRole.Kierownik,
            Token = GenerateToken(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            IsUsed = false
        };

        context.EmployeeInvitations.Add(invitation1);
        context.EmployeeInvitations.Add(invitation2);
        await context.SaveChangesAsync();

        // Act - set tenant filter
        context.SetTenant(tenant1Id);
        var tenant1Invitations = await context.EmployeeInvitations.ToListAsync();

        // Assert
        tenant1Invitations.Should().HaveCount(1);
        tenant1Invitations[0].Email.Should().Be("tenant1@test.com");
    }

    [Fact]
    public async Task CreateEmployeeFromInvitation_ShouldWorkCorrectly()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        var token = GenerateToken();
        
        var invitation = new EmployeeInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = "new.worker@company.com",
            FullName = "Anna Nowak",
            Role = EmployeeRole.Kierownik,
            Token = token,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            IsUsed = false
        };

        context.EmployeeInvitations.Add(invitation);
        await context.SaveChangesAsync();

        // Act - simulate employee registration
        context.SetTenant(tenantId);
        
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = invitation.FullName ?? "Anna Nowak",
            Email = invitation.Email,
            Telephone = "+48123456789",
            City = "Warszawa",
            Position = "Pracownik",
            Role = invitation.Role,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Employees.Add(employee);

        // Mark invitation as used
        invitation.IsUsed = true;
        invitation.UsedAtUtc = DateTime.UtcNow;
        invitation.CreatedEmployeeId = employee.Id;

        await context.SaveChangesAsync();

        // Assert
        var savedEmployee = await context.Employees.FirstOrDefaultAsync(e => e.Email == "new.worker@company.com");
        savedEmployee.Should().NotBeNull();
        savedEmployee!.FullName.Should().Be("Anna Nowak");
        savedEmployee.Role.Should().Be(EmployeeRole.Kierownik);

        var usedInvitation = await context.EmployeeInvitations.FindAsync(invitation.Id);
        usedInvitation!.IsUsed.Should().BeTrue();
        usedInvitation.CreatedEmployeeId.Should().Be(employee.Id);
    }

    [Theory]
    [InlineData(EmployeeRole.Pracownik)]
    [InlineData(EmployeeRole.Kierownik)]
    [InlineData(EmployeeRole.Manager)]
    public async Task AllEmployeeRoles_ShouldBeSupported(EmployeeRole role)
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        
        var invitation = new EmployeeInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = $"{role.ToString().ToLower()}@test.com",
            Role = role,
            Token = GenerateToken(),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            IsUsed = false
        };

        // Act
        context.EmployeeInvitations.Add(invitation);
        await context.SaveChangesAsync();

        // Assert
        var saved = await context.EmployeeInvitations.FindAsync(invitation.Id);
        saved.Should().NotBeNull();
        saved!.Role.Should().Be(role);
    }

    private static string GenerateToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("/", "_")
            .Replace("+", "-")
            .TrimEnd('=');
    }
}
