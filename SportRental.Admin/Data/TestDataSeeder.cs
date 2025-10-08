using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using System.Text.Json;

namespace SportRental.Admin.Data;

/// <summary>
/// Seeds test data for development and testing purposes
/// Loads data from test-data.json and creates tenants, products, and customers
/// </summary>
public class TestDataSeeder
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<TestDataSeeder> _logger;

    public TestDataSeeder(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ILogger<TestDataSeeder> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        try
        {
            _logger.LogInformation("🌱 Starting test data seeding...");

            // Load test data from JSON
            var testDataPath = Path.Combine(Directory.GetCurrentDirectory(), "test-data.json");
            if (!File.Exists(testDataPath))
            {
                // Try parent directory (solution root)
                testDataPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "test-data.json");
            }

            if (!File.Exists(testDataPath))
            {
                _logger.LogWarning("⚠️  test-data.json not found, skipping seeding");
                return;
            }

            var jsonContent = await File.ReadAllTextAsync(testDataPath);
            var testData = JsonSerializer.Deserialize<TestDataModel>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (testData == null)
            {
                _logger.LogWarning("⚠️  Failed to parse test-data.json");
                return;
            }

            await using var db = await _dbFactory.CreateDbContextAsync();

            // Check if already seeded
            if (await db.Tenants.AnyAsync())
            {
                _logger.LogInformation("ℹ️  Database already contains data, skipping seeding");
                return;
            }

            _logger.LogInformation($"📦 Seeding {testData.Tenants?.Count ?? 0} tenants...");

            foreach (var tenantData in testData.Tenants ?? [])
            {
                // Create Tenant
                var tenant = new Tenant
                {
                    Id = Guid.NewGuid(),
                    Name = tenantData.Name ?? "Unknown Tenant",
                    PrimaryColorHex = tenantData.PrimaryColor,
                    SecondaryColorHex = tenantData.SecondaryColor,
                    CreatedAtUtc = DateTime.UtcNow
                };

                await db.Tenants.AddAsync(tenant);
                _logger.LogInformation($"  ✅ Created tenant: {tenant.Name}");

                // Create CompanyInfo
                if (tenantData.CompanyInfo != null)
                {
                    var companyInfo = new CompanyInfo
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        Name = tenant.Name,
                        Address = tenantData.CompanyInfo.Address,
                        PhoneNumber = tenantData.CompanyInfo.PhoneNumber,
                        Email = tenantData.CompanyInfo.Email,
                        NIP = tenantData.CompanyInfo.Nip,
                        REGON = tenantData.CompanyInfo.Regon,
                        LegalForm = tenantData.CompanyInfo.LegalForm,
                        OpeningHours = tenantData.CompanyInfo.OpeningHours,
                        Description = tenantData.CompanyInfo.Description,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    await db.CompanyInfos.AddAsync(companyInfo);
                    _logger.LogInformation($"     ✅ Created CompanyInfo with NIP: {companyInfo.NIP}, REGON: {companyInfo.REGON}");
                }

                // Create Products
                if (tenantData.Products != null)
                {
                    foreach (var productData in tenantData.Products)
                    {
                        var product = new Product
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenant.Id,
                            Name = productData.Name ?? "Unknown Product",
                            Sku = productData.Sku ?? Guid.NewGuid().ToString()[..8],
                            Category = productData.Category,
                            DailyPrice = productData.DailyPrice ?? 50m,
                            AvailableQuantity = productData.AvailableQuantity ?? 1,
                            IsActive = true,
                            Available = true,
                            CreatedAtUtc = DateTime.UtcNow
                        };

                        await db.Products.AddAsync(product);
                    }
                    _logger.LogInformation($"     ✅ Created {tenantData.Products.Count} products");
                }

                // Create Customers (shared across tenants for testing)
                if (testData.Customers != null)
                {
                    foreach (var customerData in testData.Customers)
                    {
                        // Check if customer already exists for this tenant
                        var existingCustomer = await db.Customers
                            .FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.Email == customerData.Email);

                        if (existingCustomer == null)
                        {
                            var customer = new Customer
                            {
                                Id = Guid.NewGuid(),
                                TenantId = tenant.Id,
                                FullName = customerData.FullName ?? "Unknown Customer",
                                Email = customerData.Email ?? $"customer{Guid.NewGuid()}@example.com",
                                PhoneNumber = customerData.PhoneNumber,
                                DocumentNumber = customerData.DocumentNumber,
                                CreatedAtUtc = DateTime.UtcNow
                            };

                            await db.Customers.AddAsync(customer);
                        }
                    }
                    _logger.LogInformation($"     ✅ Created {testData.Customers.Count} customers");
                }
            }

            await db.SaveChangesAsync();

            _logger.LogInformation("🎉 Test data seeding completed successfully!");
            _logger.LogInformation("");
            _logger.LogInformation("📋 Seeded data summary:");
            _logger.LogInformation($"   • Tenants: {await db.Tenants.CountAsync()}");
            _logger.LogInformation($"   • CompanyInfos: {await db.CompanyInfos.CountAsync()}");
            _logger.LogInformation($"   • Products: {await db.Products.CountAsync()}");
            _logger.LogInformation($"   • Customers: {await db.Customers.CountAsync()}");
            _logger.LogInformation("");
            _logger.LogInformation("💡 You can now test the application with realistic data!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error seeding test data");
            throw;
        }
    }

    // Models for JSON deserialization
    private class TestDataModel
    {
        public List<TenantData>? Tenants { get; set; }
        public List<CustomerData>? Customers { get; set; }
    }

    private class TenantData
    {
        public string? Name { get; set; }
        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public CompanyInfoData? CompanyInfo { get; set; }
        public List<ProductData>? Products { get; set; }
    }

    private class CompanyInfoData
    {
        public string? Address { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Nip { get; set; }
        public string? Regon { get; set; }
        public string? LegalForm { get; set; }
        public string? OpeningHours { get; set; }
        public string? Description { get; set; }
    }

    private class ProductData
    {
        public string? Name { get; set; }
        public string? Sku { get; set; }
        public decimal? DailyPrice { get; set; }
        public string? Category { get; set; }
        public int? AvailableQuantity { get; set; }
    }

    private class CustomerData
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? DocumentNumber { get; set; }
    }
}
