using SportRental.Infrastructure.Domain;
using FluentAssertions;

namespace SportRental.Admin.Tests.Domain;

public class CompanyInfoTests
{
    [Fact]
    public void CompanyInfo_DefaultConstructor_ShouldSetDefaultValues()
    {
        // Act
        var companyInfo = new CompanyInfo();

        // Assert
        companyInfo.Id.Should().Be(Guid.Empty); // Default Guid value
        companyInfo.TenantId.Should().Be(Guid.Empty); // Default Guid value
        companyInfo.LegalForm.Should().Be(string.Empty);
        companyInfo.Lat.Should().Be(0);
        companyInfo.Lon.Should().Be(0);
        companyInfo.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        companyInfo.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void CompanyInfo_SetProperties_ShouldUpdateCorrectly()
    {
        // Arrange
        var companyInfo = new CompanyInfo();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Act
        companyInfo.TenantId = tenantId;
        companyInfo.Name = "SportRental Sp. z o.o.";
        companyInfo.Address = "ul. Sportowa 123, 00-001 Warszawa";
        companyInfo.NIP = "1234567890";
        companyInfo.LegalForm = "Spółka z ograniczoną odpowiedzialnością";
        companyInfo.Email = "info@sportrental.pl";
        companyInfo.PhoneNumber = "+48123456789";
        companyInfo.OpeningHours = "Pon-Pt: 9:00-18:00, Sob: 10:00-16:00";
        companyInfo.Description = "Wypożyczalnia sprzętu sportowego";
        companyInfo.ExtraInfo = "Obsługujemy karty płatnicze";
        companyInfo.IdAdmin = Guid.NewGuid();
        companyInfo.AdminEmail = "admin@sportrental.pl";
        companyInfo.AdminName = "Jan Kowalski";
        companyInfo.Lat = 52.2297;
        companyInfo.Lon = 21.0122;
        companyInfo.CreatedAtUtc = now;
        companyInfo.UpdatedAtUtc = now.AddHours(1);

        // Assert
        companyInfo.TenantId.Should().Be(tenantId);
        companyInfo.Name.Should().Be("SportRental Sp. z o.o.");
        companyInfo.Address.Should().Be("ul. Sportowa 123, 00-001 Warszawa");
        companyInfo.NIP.Should().Be("1234567890");
        companyInfo.LegalForm.Should().Be("Spółka z ograniczoną odpowiedzialnością");
        companyInfo.Email.Should().Be("info@sportrental.pl");
        companyInfo.PhoneNumber.Should().Be("+48123456789");
        companyInfo.OpeningHours.Should().Be("Pon-Pt: 9:00-18:00, Sob: 10:00-16:00");
        companyInfo.Description.Should().Be("Wypożyczalnia sprzętu sportowego");
        companyInfo.ExtraInfo.Should().Be("Obsługujemy karty płatnicze");
        companyInfo.AdminEmail.Should().Be("admin@sportrental.pl");
        companyInfo.AdminName.Should().Be("Jan Kowalski");
        companyInfo.Lat.Should().Be(52.2297);
        companyInfo.Lon.Should().Be(21.0122);
        companyInfo.CreatedAtUtc.Should().Be(now);
        companyInfo.UpdatedAtUtc.Should().Be(now.AddHours(1));
    }

    [Fact]
    public void CompanyInfo_SmsTemplates_ShouldSetCorrectly()
    {
        // Arrange
        var companyInfo = new CompanyInfo();

        // Act
        companyInfo.SmsThanksText = "Dziękujemy za wypożyczenie!";
        companyInfo.SmsReminderText = "Przypominamy o zwrocie sprzętu";
        companyInfo.SmsReminderText2 = "Ostateczne przypomnienie";
        companyInfo.SmsReminderText3 = "Pilne: zwrot sprzętu";

        // Assert
        companyInfo.SmsThanksText.Should().Be("Dziękujemy za wypożyczenie!");
        companyInfo.SmsReminderText.Should().Be("Przypominamy o zwrocie sprzętu");
        companyInfo.SmsReminderText2.Should().Be("Ostateczne przypomnienie");
        companyInfo.SmsReminderText3.Should().Be("Pilne: zwrot sprzętu");
    }

    [Fact]
    public void CompanyInfo_EmailTemplates_ShouldSetCorrectly()
    {
        // Arrange
        var companyInfo = new CompanyInfo();

        // Act
        companyInfo.EmailThanksText = "Dziękujemy za skorzystanie z naszych usług!";
        companyInfo.EmailReminderText = "Przypominamy o zbliżającym się terminie zwrotu";
        companyInfo.EmailReminderText2 = "Drugie przypomnienie o zwrocie";
        companyInfo.EmailReminderText3 = "Ostateczne wezwanie do zwrotu";

        // Assert
        companyInfo.EmailThanksText.Should().Be("Dziękujemy za skorzystanie z naszych usług!");
        companyInfo.EmailReminderText.Should().Be("Przypominamy o zbliżającym się terminie zwrotu");
        companyInfo.EmailReminderText2.Should().Be("Drugie przypomnienie o zwrocie");
        companyInfo.EmailReminderText3.Should().Be("Ostateczne wezwanie do zwrotu");
    }

    [Fact]
    public void CompanyInfo_OptionalFields_CanBeNull()
    {
        // Arrange
        var companyInfo = new CompanyInfo();

        // Assert
        companyInfo.Name.Should().BeNull();
        companyInfo.Address.Should().BeNull();
        companyInfo.NIP.Should().BeNull();
        companyInfo.Email.Should().BeNull();
        companyInfo.PhoneNumber.Should().BeNull();
        companyInfo.OpeningHours.Should().BeNull();
        companyInfo.Description.Should().BeNull();
        companyInfo.ExtraInfo.Should().BeNull();
        companyInfo.IdAdmin.Should().BeNull();
        companyInfo.AdminEmail.Should().BeNull();
        companyInfo.AdminName.Should().BeNull();
        companyInfo.SmsThanksText.Should().BeNull();
        companyInfo.EmailThanksText.Should().BeNull();
        companyInfo.Tenant.Should().BeNull();
    }

    [Fact]
    public void CompanyInfo_NavigationProperties_CanBeSet()
    {
        // Arrange
        var companyInfo = new CompanyInfo();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Test Tenant" };

        // Act
        companyInfo.Tenant = tenant;

        // Assert
        companyInfo.Tenant.Should().Be(tenant);
    }

    [Fact]
    public void CompanyInfo_GpsCoordinates_ShouldHandleNullValues()
    {
        // Arrange
        var companyInfo = new CompanyInfo();

        // Act
        companyInfo.Lat = null;
        companyInfo.Lon = null;

        // Assert
        companyInfo.Lat.Should().BeNull();
        companyInfo.Lon.Should().BeNull();
    }

    [Theory]
    [InlineData(52.2297, 21.0122)] // Warsaw
    [InlineData(50.0647, 19.9450)] // Krakow
    [InlineData(54.3520, 18.6466)] // Gdansk
    public void CompanyInfo_GpsCoordinates_ShouldAcceptValidCoordinates(double lat, double lon)
    {
        // Arrange
        var companyInfo = new CompanyInfo();

        // Act
        companyInfo.Lat = lat;
        companyInfo.Lon = lon;

        // Assert
        companyInfo.Lat.Should().Be(lat);
        companyInfo.Lon.Should().Be(lon);
    }
}

public class ProductCategoryTests
{
    [Fact]
    public void ProductCategory_DefaultConstructor_ShouldSetDefaultValues()
    {
        // Act
        var category = new ProductCategory();

        // Assert
        category.Id.Should().Be(Guid.Empty); // Default Guid value
        category.TenantId.Should().Be(Guid.Empty); // Default Guid value
        category.Name.Should().Be(string.Empty);
        category.SortOrder.Should().Be(0);
        category.IsDeleted.Should().BeFalse();
        category.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        category.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void ProductCategory_SetProperties_ShouldUpdateCorrectly()
    {
        // Arrange
        var category = new ProductCategory();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Act
        category.TenantId = tenantId;
        category.Name = "Narty";
        category.Description = "Narty zjazdowe i biegowe";
        category.SortOrder = 5;
        category.IsDeleted = false;
        category.CreatedAtUtc = now;
        category.UpdatedAtUtc = now.AddMinutes(30);

        // Assert
        category.TenantId.Should().Be(tenantId);
        category.Name.Should().Be("Narty");
        category.Description.Should().Be("Narty zjazdowe i biegowe");
        category.SortOrder.Should().Be(5);
        category.IsDeleted.Should().BeFalse();
        category.CreatedAtUtc.Should().Be(now);
        category.UpdatedAtUtc.Should().Be(now.AddMinutes(30));
    }

    [Fact]
    public void ProductCategory_OptionalFields_CanBeNull()
    {
        // Arrange
        var category = new ProductCategory();

        // Assert
        category.Description.Should().BeNull();
        category.UpdatedAtUtc.Should().BeNull();
        category.Tenant.Should().BeNull();
        category.Products.Should().BeNull();
    }

    [Fact]
    public void ProductCategory_NavigationProperties_CanBeSet()
    {
        // Arrange
        var category = new ProductCategory();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Test Tenant" };
        var products = new List<Product>
        {
            new Product { Id = Guid.NewGuid(), Name = "Product 1" },
            new Product { Id = Guid.NewGuid(), Name = "Product 2" }
        };

        // Act
        category.Tenant = tenant;
        category.Products = products;

        // Assert
        category.Tenant.Should().Be(tenant);
        category.Products.Should().BeEquivalentTo(products);
        category.Products.Should().HaveCount(2);
    }

    [Fact]
    public void ProductCategory_SoftDelete_ShouldWork()
    {
        // Arrange
        var category = new ProductCategory
        {
            Name = "Test Category",
            IsDeleted = false
        };

        // Act
        category.IsDeleted = true;
        category.UpdatedAtUtc = DateTime.UtcNow;

        // Assert
        category.IsDeleted.Should().BeTrue();
        category.UpdatedAtUtc.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(-1)]
    public void ProductCategory_SortOrder_ShouldAcceptVariousValues(int sortOrder)
    {
        // Arrange
        var category = new ProductCategory();

        // Act
        category.SortOrder = sortOrder;

        // Assert
        category.SortOrder.Should().Be(sortOrder);
    }
}

public class ProductCategoryDefaultsTests
{
    [Fact]
    public void ProductCategoryDefaults_ShouldContainExpectedCategories()
    {
        // Assert
        ProductCategoryDefaults.DefaultTypes.Should().NotBeEmpty();
        ProductCategoryDefaults.DefaultTypes.Should().HaveCount(12);
        
        ProductCategoryDefaults.DefaultTypes[0].Should().Be("Deskorolka");
        ProductCategoryDefaults.DefaultTypes[1].Should().Be("Snowboard");
        ProductCategoryDefaults.DefaultTypes[2].Should().Be("Narty");
        ProductCategoryDefaults.DefaultTypes[3].Should().Be("Buty");
        ProductCategoryDefaults.DefaultTypes[4].Should().Be("Kask");
        ProductCategoryDefaults.DefaultTypes[5].Should().Be("Kijki");
        ProductCategoryDefaults.DefaultTypes[6].Should().Be("Kurtka");
        ProductCategoryDefaults.DefaultTypes[7].Should().Be("Spodnie");
        ProductCategoryDefaults.DefaultTypes[8].Should().Be("Gogle");
        ProductCategoryDefaults.DefaultTypes[9].Should().Be("Rękawice");
        ProductCategoryDefaults.DefaultTypes[10].Should().Be("Rowery");
        ProductCategoryDefaults.DefaultTypes[11].Should().Be("Inne");
    }

    [Fact]
    public void ProductCategoryDefaults_AllKeysSequential_ShouldBeTrue()
    {
        // Arrange
        var expectedKeys = Enumerable.Range(0, 12).ToList();

        // Act
        var actualKeys = ProductCategoryDefaults.DefaultTypes.Keys.OrderBy(k => k).ToList();

        // Assert
        actualKeys.Should().BeEquivalentTo(expectedKeys);
    }

    [Fact]
    public void ProductCategoryDefaults_AllValuesNotEmpty_ShouldBeTrue()
    {
        // Assert
        ProductCategoryDefaults.DefaultTypes.Values.Should().AllSatisfy(value =>
        {
            value.Should().NotBeNullOrWhiteSpace();
        });
    }

    [Theory]
    [InlineData(0, "Deskorolka")]
    [InlineData(2, "Narty")]
    [InlineData(10, "Rowery")]
    [InlineData(11, "Inne")]
    public void ProductCategoryDefaults_SpecificMappings_ShouldBeCorrect(int key, string expectedValue)
    {
        // Assert
        ProductCategoryDefaults.DefaultTypes[key].Should().Be(expectedValue);
    }
}