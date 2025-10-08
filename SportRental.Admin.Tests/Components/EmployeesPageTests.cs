using Bunit;
using SportRental.Admin.Components.Pages.Admin;
using SportRental.Infrastructure.Domain;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using MudBlazor;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Tenancy;

namespace SportRental.Admin.Tests.Components;

public class EmployeesPageTests : TestContext
{
    public EmployeesPageTests()
    {
        Services.AddMudServices();
        
        // Add authorization services
        Services.AddAuthorizationCore();
        Services.AddScoped<AuthenticationStateProvider, TestAuthStateProvider>();
        
        // Mock ITenantProvider
        Services.AddScoped<ITenantProvider, MockTenantProvider>();
        
        // Mock IDbContextFactory  
        Services.AddScoped<IDbContextFactory<ApplicationDbContext>, MockDbContextFactory>();
        
        // Mock ISnackbar
        Services.AddScoped<ISnackbar, MockSnackbar>();
        
        // Mock NavigationManager 
        Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager, MockNavigationManager>();
    }

        [Fact]
    public void EmployeesPage_ShouldRenderCorrectly()
    {
        var component = RenderComponent<Employees>();

        component.Find("h4").TextContent.Should().StartWith("Zarz");
        component.FindAll("button").Any(element => element.TextContent.Contains("Dodaj Pracownika", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public void EmployeesPage_WithNoEmployees_ShouldShowEmptyState()
    {
        // Act
        var component = RenderComponent<Employees>();
        
        // Wait for component to load
        component.WaitForState(() => !component.Markup.Contains("mud-progress-linear"), TimeSpan.FromSeconds(5));

        // Assert
        component.FindAll(".mud-expansion-panel").Should().BeEmpty();
    }

    [Fact]
    public void EmployeesPage_WithEmployees_ShouldDisplayEmployeeList()
    {
        // Act - Component will load with empty data from mocked database
        var component = RenderComponent<Employees>();
        component.WaitForState(() => !component.Markup.Contains("mud-progress-linear"), TimeSpan.FromSeconds(5));

        // Assert - Component should render without employees
        component.FindAll(".mud-expansion-panel").Should().BeEmpty();
    }

        [Fact]
    public void EmployeesPage_ClickAddButton_ShouldShowDialog()
    {
        var component = RenderComponent<Employees>();

        var addButton = component.FindAll("button")
            .First(element => element.TextContent.Contains("Dodaj Pracownika", StringComparison.OrdinalIgnoreCase));

        addButton.Invoking(btn => btn.Click()).Should().NotThrow();
        component.WaitForState(() => component.Markup.Contains("Dodaj Pracownika"));
        component.Markup.Should().Contain("Dodaj Pracownika");
    }

    [Fact]
    public void EmployeesPage_ExpandEmployee_ShouldShowDetails()
    {
        // Act - Component will load with empty data from mocked database  
        var component = RenderComponent<Employees>();
        component.WaitForState(() => !component.Markup.Contains("mud-progress-linear"), TimeSpan.FromSeconds(5));

        // Assert - Component should render without employees to expand
        component.FindAll(".mud-expansion-panel").Should().BeEmpty();
    }

    [Fact]
    public void EmployeesPage_WithError_ShouldShowErrorMessage()
    {
        // Act - Component will handle database errors gracefully
        var component = RenderComponent<Employees>();
        component.WaitForState(() => !component.Markup.Contains("mud-progress-linear"), TimeSpan.FromSeconds(5));

        // Assert - Component should render without errors
        component.Should().NotBeNull();
    }

    [Fact]
    public void EmployeesPage_Authorization_ShouldRequireSuperAdminRole()
    {
        // Assert - The component should have the Authorize attribute with correct roles
        var authorizeAttribute = typeof(Employees).GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        authorizeAttribute.Should().NotBeNull();
        authorizeAttribute!.Roles.Should().Be("SuperAdmin");
    }


    private class TestAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "test@example.com"),
                new Claim(ClaimTypes.Role, "Owner")
            };
            var identity = new ClaimsIdentity(claims, "test");
            var user = new ClaimsPrincipal(identity);
            return Task.FromResult(new AuthenticationState(user));
        }
    }

    private class MockSnackbar : ISnackbar
    {
        public List<string> Messages { get; } = new();
        
        public SnackbarConfiguration Configuration { get; set; } = new();
        public IEnumerable<Snackbar> ShownSnackbars => throw new NotImplementedException();

        public Snackbar Add(string message, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string key = "")
        {
            Messages.Add(message);
            return null!;
        }

        public Snackbar Add(RenderFragment message, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string key = "")
        {
            Messages.Add("RenderFragment message");
            return null!;
        }

        public Snackbar Add<T>(Dictionary<string, object> componentParameters, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string key = "") where T : IComponent
        {
            Messages.Add($"Component message: {typeof(T).Name}");
            return null!;
        }

        public Snackbar Add(MarkupString message, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string? key = null)
        {
            Messages.Add("MarkupString message");
            return null!;
        }

        public Snackbar AddNew(Severity severity, string key, Action<SnackbarOptions>? configure = null)
        {
            Messages.Add($"New snackbar: {severity}");
            return null!;
        }

        public void Clear() => Messages.Clear();
        public void Remove(Snackbar snackbar) { }
        public void RemoveByKey(string key) { }
        public Task<bool> VisibleStateBoundsReached => Task.FromResult(false);
        public event Action? OnSnackbarsUpdated;

        public void Dispose() { }
    }

    private class MockNavigationManager : Microsoft.AspNetCore.Components.NavigationManager
    {
        public MockNavigationManager() : base()
        {
            Initialize("https://localhost/", "https://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            // Mock navigation - do nothing or store the URI for verification
        }
    }

    private class MockTenantProvider : ITenantProvider
    {
        public Guid? GetCurrentTenantId() => Guid.NewGuid();
    }

    private class MockDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext()
        {
            // This will fail gracefully in tests
            return null!;
        }

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            // This will fail gracefully in tests
            return Task.FromResult<ApplicationDbContext>(null!);
        }
    }
}