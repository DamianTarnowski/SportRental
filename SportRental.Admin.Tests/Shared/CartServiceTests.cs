using FluentAssertions;
using Microsoft.JSInterop;
using Moq;
using SportRental.Shared.Models;
using SportRental.Shared.Services;
using SportRental.Shared.Time;
using Xunit;

namespace SportRental.Admin.Tests.Shared;

public class CartServiceTests
{
    [Fact]
    public async Task AddToCartAsync_WithDates_SetsDatesSecuresHoldAndStoresImage()
    {
        // Arrange
        var js = CreateJsRuntimeMock();
        var captureRequests = new List<CreateHoldRequest>();
        var api = new Mock<IApiService>();
        api.Setup(a => a.CreateHoldAsync(It.IsAny<CreateHoldRequest>()))
            .ReturnsAsync((CreateHoldRequest req) =>
            {
                captureRequests.Add(req);
                return new CreateHoldResponse
                {
                    Id = Guid.NewGuid(),
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
                };
            });
        api.Setup(a => a.DeleteHoldAsync(It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        api.Setup(a => a.GetProductsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ProductDto>());

        var service = await CreateServiceAsync(js.Object, api.Object);
        var start = DateTime.Today.AddDays(2);
        var end = start.AddDays(3);
        var product = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TenantName = "Bike Point",
            PickupAddress = "ul. Wiślna 7",
            City = "Kraków",
            Name = "Mountain Bike",
            FullImageUrl = "https://cdn/img/bike.jpg",
            DailyPrice = 120m,
            AvailableQuantity = 5,
            IsAvailable = true
        };

        // Act
        await service.AddToCartAsync(product, 1, start, end);
        var holdSuccess = await service.EnsureHoldsAsync();

        // Assert
        holdSuccess.Should().BeTrue();
        captureRequests.Should().ContainSingle();
        captureRequests[0].ProductId.Should().Be(product.Id);
        captureRequests[0].StartDateUtc.Should().Be(PolishRentalTime.ToUtc(start));
        captureRequests[0].EndDateUtc.Should().Be(PolishRentalTime.ToUtc(end));

        var cartItem = service.GetCart().Items.Should().ContainSingle().Subject;
        cartItem.StartDate.Should().Be(start);
        cartItem.EndDate.Should().Be(end);
        cartItem.ProductImageUrl.Should().Be(product.FullImageUrl);
        cartItem.TenantId.Should().Be(product.TenantId);
        cartItem.TenantName.Should().Be(product.TenantName);
        cartItem.PickupAddress.Should().Be(product.PickupAddress);
        cartItem.PickupCity.Should().Be(product.City);
        cartItem.HoldId.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateDatesAsync_ReleasesPreviousHoldAndCreatesNewOne()
    {
        // Arrange
        var js = CreateJsRuntimeMock();
        var api = new Mock<IApiService>();

        var holdIds = new Queue<Guid>(new[] { Guid.NewGuid(), Guid.NewGuid() });
        api.Setup(a => a.CreateHoldAsync(It.IsAny<CreateHoldRequest>()))
            .ReturnsAsync(() => new CreateHoldResponse
            {
                Id = holdIds.Dequeue(),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
            });
        api.Setup(a => a.DeleteHoldAsync(It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);
        api.Setup(a => a.GetProductsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ProductDto>());

        var service = await CreateServiceAsync(js.Object, api.Object);
        var product = new ProductDto
        {
            Id = Guid.NewGuid(),
            Name = "SUP Board",
            DailyPrice = 90m,
            IsAvailable = true,
            AvailableQuantity = 3
        };

        await service.AddToCartAsync(
            product,
            1,
            DateTime.Today.AddDays(1),
            DateTime.Today.AddDays(3));
        await service.EnsureHoldsAsync();
        var item = service.GetCart().Items.Single();
        var initialHold = item.HoldId;
        initialHold.Should().NotBeNull();

        // Act
        var newStart = DateTime.Today.AddDays(5);
        var newEnd = newStart.AddDays(2);
        await service.UpdateDatesAsync(product.Id, newStart, newEnd);

        // Assert
        api.Verify(a => a.DeleteHoldAsync(initialHold!.Value, It.IsAny<string?>()), Times.Once);
        item.StartDate.Should().Be(newStart);
        item.EndDate.Should().Be(newEnd);
        item.HoldId.Should().NotBeNull();
        item.HoldId!.Value.Should().NotBe(initialHold!.Value);
    }

    [Fact]
    public async Task UpdateQuantityAsync_ReleasesPreviousHoldAndCreatesReplacementWithNewQuantity()
    {
        var js = CreateJsRuntimeMock();
        var api = new Mock<IApiService>();
        var firstHoldId = Guid.NewGuid();
        var replacementHoldId = Guid.NewGuid();
        var holdIds = new Queue<Guid>(new[] { firstHoldId, replacementHoldId });
        var holdRequests = new List<CreateHoldRequest>();
        api.Setup(a => a.CreateHoldAsync(It.IsAny<CreateHoldRequest>()))
            .ReturnsAsync((CreateHoldRequest request) =>
            {
                holdRequests.Add(request);
                return new CreateHoldResponse
                {
                    Id = holdIds.Dequeue(),
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
                };
            });
        api.Setup(a => a.DeleteHoldAsync(It.IsAny<Guid>(), It.IsAny<string?>())).ReturnsAsync(true);

        var service = await CreateServiceAsync(js.Object, api.Object);
        var product = new ProductDto
        {
            Id = Guid.NewGuid(),
            Name = "Rower",
            DailyPrice = 100m,
            IsAvailable = true,
            AvailableQuantity = 5
        };
        var start = DateTime.Today.AddDays(1);
        var end = start.AddDays(2);
        await service.AddToCartAsync(product, 1, start, end);

        await service.UpdateQuantityAsync(product.Id, 3);

        api.Verify(a => a.DeleteHoldAsync(firstHoldId, It.IsAny<string?>()), Times.Once);
        holdRequests.Should().HaveCount(2);
        holdRequests[0].Quantity.Should().Be(1);
        holdRequests[1].Quantity.Should().Be(3);
        var item = service.GetCart().Items.Should().ContainSingle().Subject;
        item.Quantity.Should().Be(3);
        item.HoldId.Should().Be(replacementHoldId);
    }

    [Fact]
    public async Task TenantRentalTerms_AreSharedInsideShopButIndependentBetweenShops()
    {
        var js = CreateJsRuntimeMock();
        var api = new Mock<IApiService>();
        api.Setup(a => a.CreateHoldAsync(It.IsAny<CreateHoldRequest>()))
            .ReturnsAsync(() => new CreateHoldResponse
            {
                Id = Guid.NewGuid(),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
            });
        api.Setup(a => a.DeleteHoldAsync(It.IsAny<Guid>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        var service = await CreateServiceAsync(js.Object, api.Object);
        var firstTenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var firstStart = DateTime.Today.AddDays(2).AddHours(9);
        var firstEnd = firstStart.AddDays(2);
        var differentRequestedStart = firstStart.AddDays(8);
        var secondTenantStart = firstStart.AddDays(4);

        await service.AddToCartAsync(CreateProduct(firstTenantId, "Narty"), 1, firstStart, firstEnd);
        await service.AddToCartAsync(
            CreateProduct(firstTenantId, "Kask"),
            1,
            differentRequestedStart,
            differentRequestedStart.AddDays(1));
        await service.AddToCartAsync(
            CreateProduct(secondTenantId, "Rower"),
            1,
            secondTenantStart,
            secondTenantStart.AddDays(1));

        var firstTenantItems = service.GetCart().Items
            .Where(item => item.TenantId == firstTenantId)
            .ToList();
        firstTenantItems.Should().HaveCount(2);
        firstTenantItems.Should().OnlyContain(item =>
            item.StartDate == firstStart && item.EndDate == firstEnd);

        var hourlyStart = firstStart.AddDays(1);
        await service.UpdateTenantRentalTermsAsync(
            firstTenantId,
            hourlyStart,
            hourlyStart.AddHours(4),
            RentalTypeDto.Hourly,
            4);

        firstTenantItems.Should().OnlyContain(item =>
            item.StartDate == hourlyStart &&
            item.EndDate == hourlyStart.AddHours(4) &&
            item.RentalType == RentalTypeDto.Hourly &&
            item.HoursRented == 4 &&
            item.HoldId.HasValue);
        var secondTenantItem = service.GetCart().Items.Single(item => item.TenantId == secondTenantId);
        secondTenantItem.StartDate.Should().Be(secondTenantStart);
        secondTenantItem.RentalType.Should().Be(RentalTypeDto.Daily);
    }

    [Fact]
    public async Task AddToCartAsync_SecondProductInTenantInheritsCompleteCanonicalTerms()
    {
        var js = CreateJsRuntimeMock();
        var api = new Mock<IApiService>();
        api.Setup(a => a.CreateHoldAsync(It.IsAny<CreateHoldRequest>()))
            .ReturnsAsync(() => new CreateHoldResponse
            {
                Id = Guid.NewGuid(),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
            });
        api.Setup(a => a.DeleteHoldAsync(It.IsAny<Guid>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        var service = await CreateServiceAsync(js.Object, api.Object);
        var tenantId = Guid.NewGuid();
        var canonicalStart = DateTime.Today.AddDays(2).AddHours(10);
        var canonicalEnd = canonicalStart.AddHours(4);
        var firstProduct = CreateProduct(tenantId, "Narty");
        var secondProduct = CreateProduct(tenantId, "Kask");

        await service.AddToCartAsync(firstProduct, 1, canonicalStart, canonicalEnd);
        await service.UpdateTenantRentalTermsAsync(
            tenantId,
            canonicalStart,
            canonicalEnd,
            RentalTypeDto.Hourly,
            4);

        var unrelatedRequestedStart = canonicalStart.AddDays(7);
        await service.AddToCartAsync(
            secondProduct,
            1,
            unrelatedRequestedStart,
            unrelatedRequestedStart.AddDays(2));

        service.GetCart().Items
            .Where(item => item.TenantId == tenantId)
            .Should().HaveCount(2).And.OnlyContain(item =>
                item.StartDate == canonicalStart &&
                item.EndDate == canonicalEnd &&
                item.RentalType == RentalTypeDto.Hourly &&
                item.HoursRented == 4);
    }

    [Fact]
    public async Task RefreshHoldsIfNeededAsync_UsesRefreshEndpointAndKeepsHoldSession()
    {
        var js = CreateJsRuntimeMock();
        var api = new Mock<IApiService>();
        var holdId = Guid.NewGuid();
        string? createdWithSession = null;
        string? refreshedWithSession = null;
        api.Setup(a => a.CreateHoldAsync(It.IsAny<CreateHoldRequest>()))
            .ReturnsAsync((CreateHoldRequest request) =>
            {
                createdWithSession = request.SessionId;
                return new CreateHoldResponse
                {
                    Id = holdId,
                    ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30)
                };
            });
        api.Setup(a => a.RefreshHoldAsync(holdId, It.IsAny<string>(), 10))
            .ReturnsAsync((Guid _, string sessionId, int _) =>
            {
                refreshedWithSession = sessionId;
                return new CreateHoldResponse
                {
                    Id = holdId,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
                };
            });

        var service = await CreateServiceAsync(js.Object, api.Object);
        var product = new ProductDto
        {
            Id = Guid.NewGuid(),
            Name = "Kajak",
            DailyPrice = 80m,
            IsAvailable = true,
            AvailableQuantity = 2
        };
        await service.AddToCartAsync(
            product,
            1,
            DateTime.Today.AddDays(1),
            DateTime.Today.AddDays(2));

        await service.RefreshHoldsIfNeededAsync(TimeSpan.FromMinutes(2));

        api.Verify(a => a.RefreshHoldAsync(holdId, It.IsAny<string>(), 10), Times.Once);
        api.Verify(a => a.CreateHoldAsync(It.IsAny<CreateHoldRequest>()), Times.Once);
        createdWithSession.Should().NotBeNullOrWhiteSpace();
        refreshedWithSession.Should().Be(createdWithSession);
        var item = service.GetCart().Items.Should().ContainSingle().Subject;
        item.HoldId.Should().Be(holdId);
        item.HoldExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(9));
    }

    [Fact]
    public async Task UpdateHoldExpirationsAsync_PersistsStripeExtensionAndKeepsExistingHold()
    {
        var js = CreateJsRuntimeMock();
        var api = new Mock<IApiService>();
        var holdId = Guid.NewGuid();
        api.Setup(a => a.CreateHoldAsync(It.IsAny<CreateHoldRequest>()))
            .ReturnsAsync(new CreateHoldResponse
            {
                Id = holdId,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30)
            });

        var service = await CreateServiceAsync(js.Object, api.Object);
        await service.AddToCartAsync(
            new ProductDto
            {
                Id = Guid.NewGuid(),
                Name = "Narty",
                DailyPrice = 100m,
                IsAvailable = true,
                AvailableQuantity = 2
            },
            1,
            DateTime.Today.AddDays(1),
            DateTime.Today.AddDays(2));
        var extendedUntil = DateTime.UtcNow.AddMinutes(40);

        await service.UpdateHoldExpirationsAsync([holdId], extendedUntil);
        await service.EnsureHoldsAsync();

        var item = service.GetCart().Items.Should().ContainSingle().Subject;
        item.HoldId.Should().Be(holdId);
        item.HoldExpiresAtUtc.Should().Be(extendedUntil);
        api.Verify(a => a.CreateHoldAsync(It.IsAny<CreateHoldRequest>()), Times.Once);
        js.Verify(
            runtime => runtime.InvokeAsync<object>(
                "localStorage.setItem",
                It.IsAny<object?[]>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ValidateAvailabilityAsync_ReturnsFalseWhenStockInsufficient()
    {
        // Arrange
        var js = CreateJsRuntimeMock();
        var api = new Mock<IApiService>();
        var productId = Guid.NewGuid();

        api.SetupSequence(a => a.GetProductAsync(productId))
            .ReturnsAsync(new ProductDto
            {
                Id = productId,
                Name = "Kayak",
                IsAvailable = true,
                AvailableQuantity = 5
            })
            .ReturnsAsync(new ProductDto
            {
                Id = productId,
                Name = "Kayak",
                IsAvailable = false,
                AvailableQuantity = 0
            });

        var service = await CreateServiceAsync(js.Object, api.Object);
        var product = new ProductDto
        {
            Id = productId,
            Name = "Kayak",
            DailyPrice = 70m,
            AvailableQuantity = 5,
            IsAvailable = true
        };

        await service.AddToCartAsync(product);

        // First call: should succeed
        var firstCheck = await service.ValidateAvailabilityAsync();
        firstCheck.Should().BeTrue();

        // Second call uses next sequence value (out of stock)
        var secondCheck = await service.ValidateAvailabilityAsync();
        secondCheck.Should().BeFalse();
        service.LastUnavailableProductIds.Should().Contain(productId);
    }

    [Fact]
    public async Task ValidateAvailabilityAsync_ReturnsFalseWhenApiThrows()
    {
        // Arrange
        var js = CreateJsRuntimeMock();
        var api = new Mock<IApiService>();
        api.Setup(a => a.GetProductAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var service = await CreateServiceAsync(js.Object, api.Object);
        var product = new ProductDto
        {
            Id = Guid.NewGuid(),
            Name = "Helmet",
            DailyPrice = 15m,
            AvailableQuantity = 10,
            IsAvailable = true
        };

        await service.AddToCartAsync(product);

        // Act
        var available = await service.ValidateAvailabilityAsync();

        // Assert
        available.Should().BeFalse();
        service.LastUnavailableProductIds.Should().Contain(product.Id);
    }

    private static Mock<IJSRuntime> CreateJsRuntimeMock()
    {
        var js = new Mock<IJSRuntime>();
        js.Setup(j => j.InvokeAsync<string>(It.IsAny<string>(), It.IsAny<object?[]>()))
            .ReturnsAsync(string.Empty);
        js.Setup(j => j.InvokeAsync<object>(It.IsAny<string>(), It.IsAny<object?[]>()))
            .ReturnsAsync((object?)null);
        return js;
    }

    private static ProductDto CreateProduct(Guid tenantId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        TenantName = $"Wypożyczalnia {tenantId:N}",
        PickupAddress = "ul. Testowa 1",
        City = "Kraków",
        Name = name,
        DailyPrice = 100m,
        HourlyPrice = 25m,
        IsAvailable = true,
        AvailableQuantity = 5
    };

    private static async Task<CartService> CreateServiceAsync(IJSRuntime jsRuntime, IApiService apiService)
    {
        var service = new CartService(jsRuntime, apiService);
        // allow background load from local storage to complete
        await Task.Delay(5);
        return service;
    }

    private static Task<CartService> CreateServiceAsync(Mock<IJSRuntime> jsRuntime, IApiService apiService)
        => CreateServiceAsync(jsRuntime.Object, apiService);
}
