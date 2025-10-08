using SportRental.Infrastructure.Domain;
using FluentAssertions;

namespace SportRental.Admin.Tests.Domain;

public class EmployeeTests
{
    [Fact]
    public void Employee_DefaultConstructor_ShouldSetDefaultValues()
    {
        // Act
        var employee = new Employee();

        // Assert
        employee.Id.Should().Be(Guid.Empty); // Default Guid value
        employee.FullName.Should().Be(string.Empty);
        employee.Email.Should().Be(string.Empty);
        employee.City.Should().Be(string.Empty);
        employee.Telephone.Should().Be(string.Empty);
        employee.Position.Should().Be("Pracownik");
        employee.Role.Should().Be(EmployeeRole.Pracownik);
        employee.AllRentalsNumber.Should().Be(0);
        employee.IsDeleted.Should().BeFalse();
        employee.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Employee_SetProperties_ShouldUpdateCorrectly()
    {
        // Arrange
        var employee = new Employee();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Act
        employee.TenantId = tenantId;
        employee.FullName = "Jan Kowalski";
        employee.Email = "jan.kowalski@test.com";
        employee.City = "Warszawa";
        employee.Telephone = "+48123456789";
        employee.Position = "Manager";
        employee.Role = EmployeeRole.Manager;
        employee.Comment = "Senior developer";
        employee.AllRentalsNumber = 15;
        var userId = Guid.NewGuid();
        employee.UserId = userId;
        employee.IsDeleted = false;
        employee.CreatedAtUtc = now;
        employee.UpdatedAtUtc = now.AddHours(1);

        // Assert
        employee.TenantId.Should().Be(tenantId);
        employee.FullName.Should().Be("Jan Kowalski");
        employee.Email.Should().Be("jan.kowalski@test.com");
        employee.City.Should().Be("Warszawa");
        employee.Telephone.Should().Be("+48123456789");
        employee.Position.Should().Be("Manager");
        employee.Role.Should().Be(EmployeeRole.Manager);
        employee.Comment.Should().Be("Senior developer");
        employee.AllRentalsNumber.Should().Be(15);
        employee.UserId.Should().Be(userId);
        employee.IsDeleted.Should().BeFalse();
        employee.CreatedAtUtc.Should().Be(now);
        employee.UpdatedAtUtc.Should().Be(now.AddHours(1));
    }

    [Theory]
    [InlineData(EmployeeRole.Pracownik)]
    [InlineData(EmployeeRole.Kierownik)]
    [InlineData(EmployeeRole.Manager)]
    public void Employee_Role_ShouldAcceptAllValidRoles(EmployeeRole role)
    {
        // Arrange
        var employee = new Employee();

        // Act
        employee.Role = role;

        // Assert
        employee.Role.Should().Be(role);
    }

    [Fact]
    public void Employee_RequiredFields_ShouldNotBeNull()
    {
        // Arrange
        var employee = new Employee
        {
            FullName = "Test User",
            Email = "test@example.com",
            City = "Test City",
            Telephone = "+48123456789"
        };

        // Assert
        employee.FullName.Should().NotBeNullOrEmpty();
        employee.Email.Should().NotBeNullOrEmpty();
        employee.City.Should().NotBeNullOrEmpty();
        employee.Telephone.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Employee_OptionalFields_CanBeNull()
    {
        // Arrange
        var employee = new Employee();

        // Act & Assert
        employee.Comment.Should().BeNull();
        employee.UpdatedAtUtc.Should().BeNull();
        employee.Tenant.Should().BeNull();
        employee.Permissions.Should().BeNull();
    }
}

public class EmployeePermissionsTests
{
    [Fact]
    public void EmployeePermissions_DefaultConstructor_ShouldSetDefaultValues()
    {
        // Act
        var permissions = new EmployeePermissions();

        // Assert
        permissions.Id.Should().Be(Guid.Empty); // Default Guid value
        permissions.TenantId.Should().Be(Guid.Empty); // Default Guid value
        permissions.EmployeeId.Should().Be(Guid.Empty); // Default Guid value
        
        // All permissions should default to false
        permissions.KierownikCanAddClient.Should().BeFalse();
        permissions.KierownikCanEditClient.Should().BeFalse();
        permissions.KierownikCanDeleteClient.Should().BeFalse();
        permissions.KierownikCanSeeReports.Should().BeFalse();
        
        permissions.ManagerCanAddClient.Should().BeFalse();
        permissions.ManagerCanEditClient.Should().BeFalse();
        permissions.ManagerCanDeleteClient.Should().BeFalse();
        permissions.ManagerCanSeeReports.Should().BeFalse();
        
        permissions.PracownikCanAddClient.Should().BeFalse();
        permissions.PracownikCanEditClient.Should().BeFalse();
        permissions.PracownikCanDeleteClient.Should().BeFalse();
        permissions.PracownikCanSeeReports.Should().BeFalse();
        
        permissions.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void EmployeePermissions_SetKierownikPermissions_ShouldUpdateCorrectly()
    {
        // Arrange
        var permissions = new EmployeePermissions();

        // Act
        permissions.KierownikCanAddClient = true;
        permissions.KierownikCanEditClient = true;
        permissions.KierownikCanDeleteClient = false;
        permissions.KierownikCanAddProduct = true;
        permissions.KierownikCanEditProduct = true;
        permissions.KierownikCanDeleteProduct = false;
        permissions.KierownikCanSeeReports = true;

        // Assert
        permissions.KierownikCanAddClient.Should().BeTrue();
        permissions.KierownikCanEditClient.Should().BeTrue();
        permissions.KierownikCanDeleteClient.Should().BeFalse();
        permissions.KierownikCanAddProduct.Should().BeTrue();
        permissions.KierownikCanEditProduct.Should().BeTrue();
        permissions.KierownikCanDeleteProduct.Should().BeFalse();
        permissions.KierownikCanSeeReports.Should().BeTrue();
    }

    [Fact]
    public void EmployeePermissions_SetManagerPermissions_ShouldUpdateCorrectly()
    {
        // Arrange
        var permissions = new EmployeePermissions();

        // Act
        permissions.ManagerCanAddClient = true;
        permissions.ManagerCanEditClient = true;
        permissions.ManagerCanDeleteClient = true;
        permissions.ManagerCanAddEmployee = true;
        permissions.ManagerCanEditEmployee = true;
        permissions.ManagerCanDeleteEmployee = false;
        permissions.ManagerCanSeeReports = true;

        // Assert
        permissions.ManagerCanAddClient.Should().BeTrue();
        permissions.ManagerCanEditClient.Should().BeTrue();
        permissions.ManagerCanDeleteClient.Should().BeTrue();
        permissions.ManagerCanAddEmployee.Should().BeTrue();
        permissions.ManagerCanEditEmployee.Should().BeTrue();
        permissions.ManagerCanDeleteEmployee.Should().BeFalse();
        permissions.ManagerCanSeeReports.Should().BeTrue();
    }

    [Fact]
    public void EmployeePermissions_SetPracownikPermissions_ShouldUpdateCorrectly()
    {
        // Arrange
        var permissions = new EmployeePermissions();

        // Act
        permissions.PracownikCanAddClient = false;
        permissions.PracownikCanEditClient = true;
        permissions.PracownikCanDeleteClient = false;
        permissions.PracownikCanAddRental = true;
        permissions.PracownikCanEditRental = true;
        permissions.PracownikCanDeleteRental = false;

        // Assert
        permissions.PracownikCanAddClient.Should().BeFalse();
        permissions.PracownikCanEditClient.Should().BeTrue();
        permissions.PracownikCanDeleteClient.Should().BeFalse();
        permissions.PracownikCanAddRental.Should().BeTrue();
        permissions.PracownikCanEditRental.Should().BeTrue();
        permissions.PracownikCanDeleteRental.Should().BeFalse();
    }

    [Fact]
    public void EmployeePermissions_MultipleRentalPermissions_ShouldWork()
    {
        // Arrange
        var permissions = new EmployeePermissions();

        // Act
        permissions.KierownikCanAddMultipleRental = true;
        permissions.KierownikCanEditMultipleRental = true;
        permissions.KierownikCanDeleteMultipleRental = false;
        
        permissions.ManagerCanAddMultipleRental = true;
        permissions.ManagerCanEditMultipleRental = true;
        permissions.ManagerCanDeleteMultipleRental = true;
        
        permissions.PracownikCanAddMultipleRental = false;
        permissions.PracownikCanEditMultipleRental = false;
        permissions.PracownikCanDeleteMultipleRental = false;

        // Assert
        permissions.KierownikCanAddMultipleRental.Should().BeTrue();
        permissions.KierownikCanEditMultipleRental.Should().BeTrue();
        permissions.KierownikCanDeleteMultipleRental.Should().BeFalse();
        
        permissions.ManagerCanAddMultipleRental.Should().BeTrue();
        permissions.ManagerCanEditMultipleRental.Should().BeTrue();
        permissions.ManagerCanDeleteMultipleRental.Should().BeTrue();
        
        permissions.PracownikCanAddMultipleRental.Should().BeFalse();
        permissions.PracownikCanEditMultipleRental.Should().BeFalse();
        permissions.PracownikCanDeleteMultipleRental.Should().BeFalse();
    }

    [Fact]
    public void EmployeePermissions_Timestamps_ShouldUpdateCorrectly()
    {
        // Arrange
        var permissions = new EmployeePermissions();
        var now = DateTime.UtcNow;

        // Act
        permissions.CreatedAtUtc = now;
        permissions.UpdatedAtUtc = now.AddMinutes(30);

        // Assert
        permissions.CreatedAtUtc.Should().Be(now);
        permissions.UpdatedAtUtc.Should().Be(now.AddMinutes(30));
    }

    [Fact]
    public void EmployeePermissions_NavigationProperties_CanBeSet()
    {
        // Arrange
        var permissions = new EmployeePermissions();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Test Tenant" };
        var employee = new Employee { Id = Guid.NewGuid(), FullName = "Test Employee" };

        // Act
        permissions.Tenant = tenant;
        permissions.Employee = employee;

        // Assert
        permissions.Tenant.Should().Be(tenant);
        permissions.Employee.Should().Be(employee);
    }
}
