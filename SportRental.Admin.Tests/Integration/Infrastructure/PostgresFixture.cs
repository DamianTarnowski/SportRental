using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace SportRental.Admin.Tests.Integration.Infrastructure;

/// <summary>
/// Spawnuje Docker Postgres dla testów integracyjnych. Uruchamiany raz na kolekcję,
/// kontener kasowany po wszystkich testach. Każdy test dostaje świeży DbContext;
/// schemat tworzony przez EF Core EnsureCreated (nie migrations — szybciej, bez
/// powiązań z konkretnymi migracjami).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("sr_tests")
        .WithUsername("sr_test")
        .WithPassword("sr_test_pass")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Sprawdzenie schematu raz na kolekcję — kolejne testy reset robią same.
        await using var ctx = CreateDbContext();
        await ctx.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Czyści wszystkie tabele danych domenowych zachowując schemat. Wywołać na początku
    /// każdego testu (lub w IClassFixture w setupie).
    /// </summary>
    public async Task ResetDataAsync()
    {
        await using var ctx = CreateDbContext();
        // Lista w kolejności respektującej FK (od dzieci do rodziców).
        var tables = new[]
        {
            "\"RentalItemReviews\"",
            "\"RentalReviews\"",
            "\"CustomerReviews\"",
            "\"RentalConfirmations\"",
            "\"RentalItems\"",
            "\"Rentals\"",
            "\"ReservationHolds\"",
            "\"CheckoutSessions\"",
            "\"SmsConfirmations\"",
            "\"AuditLogs\"",
            "\"ErrorLogs\"",
            "\"EmployeeInvitations\"",
            "\"Employees\"",
            "\"EmployeePermissions\"",
            "\"TenantInvitations\"",
            "\"TenantUsers\"",
            "\"RefreshTokens\"",
            "\"Customers\"",
            "\"Products\"",
            "\"ProductCategories\"",
            "\"ContractTemplates\"",
            "\"CompanyInfos\"",
            "\"Tenants\"",
            "\"AspNetUserRoles\"",
            "\"AspNetUserClaims\"",
            "\"AspNetUserLogins\"",
            "\"AspNetUserTokens\"",
            "\"AspNetUsers\"",
            "\"AspNetRoles\"",
            "\"AspNetRoleClaims\""
        };

        foreach (var t in tables)
        {
            try { await ctx.Database.ExecuteSqlRawAsync($"TRUNCATE {t} CASCADE"); }
            catch { /* tabela może nie istnieć jeśli model się różni — pomijamy */ }
        }
    }
}

/// <summary>
/// Pozwala xUnit udostępnić ten sam kontener Postgres wszystkim testom oznaczonym
/// `[Collection("postgres")]`.
/// </summary>
[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
