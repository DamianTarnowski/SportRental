using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor;
using MudBlazor.Services;
using SportRental.Admin.Components.Pages.Admin;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using System.Security.Claims;

namespace SportRental.Admin.Tests.Components;

public class EmployeesPageTests : TestContext
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;

    public EmployeesPageTests()
    {
        _dbName = $"emp-tests-{Guid.NewGuid():N}";

        Services.AddMudServices();
        Services.AddAuthorizationCore();

        Services.AddDbContextFactory<ApplicationDbContext>(opt =>
            opt.UseInMemoryDatabase(_dbName)
               .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));

        Services.AddSingleton<AuthenticationStateProvider>(_ =>
            new TestAuthStateProvider(_tenantId, role: "Owner"));

        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        // bUnit provides NavigationManager + JSRuntime via TestContext, but MudBlazor expects
        // a real-ish JSRuntime — TestContext already wires JSInterop in loose mode (no asserts).
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void EmployeesPage_RequiresOwnerOrSuperAdminRole()
    {
        var attr = typeof(Employees)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        attr.Should().NotBeNull();
        attr!.Roles.Should().Contain("Owner");
        attr.Roles.Should().Contain("SuperAdmin");
    }

    private IRenderedFragment RenderEmployeesWithProviders()
    {
        // MudBlazor requires several providers (popover/dialog/snackbar/theme) to be rendered
        // somewhere in the tree before MudSelect/MudDialog components can initialise.
        return Render(builder =>
        {
            builder.OpenComponent<MudThemeProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudPopoverProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(2);
            builder.CloseComponent();
            builder.OpenComponent<MudSnackbarProvider>(3);
            builder.CloseComponent();
            builder.OpenComponent<Employees>(4);
            builder.CloseComponent();
        });
    }

    [Fact]
    public void EmployeesPage_WithoutTenantClaim_RendersEmptyState()
    {
        // Replace default AuthStateProvider with a no-tenant variant.
        ServiceCollectionDescriptorExtensions.RemoveAll<AuthenticationStateProvider>(Services);
        Services.AddSingleton<AuthenticationStateProvider>(_ =>
            new TestAuthStateProvider(tenantId: null, role: "Owner"));

        var cut = RenderEmployeesWithProviders();

        cut.Markup.Should().Contain("Brak pracowników");
    }

    [Fact]
    public async Task EmployeesPage_DisplaysSeededEmployees()
    {
        await SeedEmployeesAsync(
            ("Jan Kowalski", "jan@test.pl", EmployeeRole.Pracownik),
            ("Anna Nowak", "anna@test.pl", EmployeeRole.Manager));

        var cut = RenderEmployeesWithProviders();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Jan Kowalski");
            cut.Markup.Should().Contain("Anna Nowak");
            cut.Markup.Should().Contain("Aktywni pracownicy (2)");
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EmployeesPage_DisplaysPendingInvitations()
    {
        await SeedInvitationsAsync(
            ("kandydat1@test.pl", EmployeeRole.Pracownik),
            ("kandydat2@test.pl", EmployeeRole.Kierownik));

        var cut = RenderEmployeesWithProviders();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Oczekujące zaproszenia");
            cut.Markup.Should().Contain("kandydat1@test.pl");
            cut.Markup.Should().Contain("kandydat2@test.pl");
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EmployeesPage_DoesNotShowOtherTenantEmployees()
    {
        var otherTenantId = Guid.NewGuid();

        await using (var db = await ResolveFactory().CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = _tenantId, Name = "Tenant A" });
            db.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Tenant B" });

            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, FullName = "Mine", Email = "mine@x.pl",
                Role = EmployeeRole.Pracownik, IsDeleted = false, CreatedAtUtc = DateTime.UtcNow
            });
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(), TenantId = otherTenantId, FullName = "Other Tenant Person",
                Email = "other@x.pl", Role = EmployeeRole.Pracownik, IsDeleted = false,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var cut = RenderEmployeesWithProviders();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Mine");
            cut.Markup.Should().NotContain("Other Tenant Person");
            cut.Markup.Should().Contain("Aktywni pracownicy (1)");
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EmployeesPage_DoesNotShowSoftDeletedEmployees()
    {
        await using var db = await ResolveFactory().CreateDbContextAsync();
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, FullName = "Active Emp",
            Email = "active@x.pl", Role = EmployeeRole.Pracownik, IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, FullName = "Deleted Emp",
            Email = "deleted@x.pl", Role = EmployeeRole.Pracownik, IsDeleted = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var cut = RenderEmployeesWithProviders();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Active Emp");
            cut.Markup.Should().NotContain("Deleted Emp");
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EmployeesPage_DoesNotShowExpiredInvitations()
    {
        await using var db = await ResolveFactory().CreateDbContextAsync();
        db.EmployeeInvitations.Add(new EmployeeInvitation
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Email = "valid@x.pl",
            Role = EmployeeRole.Pracownik, IsUsed = false,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(3),
            CreatedAtUtc = DateTime.UtcNow,
            Token = "valid-token"
        });
        db.EmployeeInvitations.Add(new EmployeeInvitation
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Email = "expired@x.pl",
            Role = EmployeeRole.Pracownik, IsUsed = false,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            Token = "expired-token"
        });
        await db.SaveChangesAsync();

        var cut = RenderEmployeesWithProviders();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("valid@x.pl");
            cut.Markup.Should().NotContain("expired@x.pl");
        }, TimeSpan.FromSeconds(5));
    }

    private IDbContextFactory<ApplicationDbContext> ResolveFactory() =>
        Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

    private async Task SeedEmployeesAsync(params (string Name, string Email, EmployeeRole Role)[] employees)
    {
        await using var db = await ResolveFactory().CreateDbContextAsync();
        foreach (var (name, email, role) in employees)
        {
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                FullName = name,
                Email = email,
                Role = role,
                IsDeleted = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task SeedInvitationsAsync(params (string Email, EmployeeRole Role)[] invitations)
    {
        await using var db = await ResolveFactory().CreateDbContextAsync();
        foreach (var (email, role) in invitations)
        {
            db.EmployeeInvitations.Add(new EmployeeInvitation
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Email = email,
                Role = role,
                IsUsed = false,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                CreatedAtUtc = DateTime.UtcNow,
                Token = Guid.NewGuid().ToString("N")
            });
        }
        await db.SaveChangesAsync();
    }

    private sealed class TestAuthStateProvider : AuthenticationStateProvider
    {
        private readonly Guid? _tenantId;
        private readonly string _role;

        public TestAuthStateProvider(Guid? tenantId, string role)
        {
            _tenantId = tenantId;
            _role = role;
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, "test@example.com"),
                new(ClaimTypes.Role, _role)
            };
            if (_tenantId.HasValue)
                claims.Add(new Claim("tenant-id", _tenantId.Value.ToString()));

            var identity = new ClaimsIdentity(claims, authenticationType: "test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
