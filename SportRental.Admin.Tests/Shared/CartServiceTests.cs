using FluentAssertions;
using Microsoft.JSInterop;
using Moq;
using SportRental.Shared.Models;
using SportRental.Shared.Services;
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
        api.Setup(a => a.DeleteHoldAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        api.Setup(a => a.GetProductsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ProductDto>());

        var service = await CreateServiceAsync(js.Object, api.Object);
        var start = DateTime.Today.AddDays(2);
        var end = start.AddDays(3);
        var product = new ProductDto
        {
            Id = Guid.NewGuid(),
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
        captureRequests[0].StartDateUtc.Should().Be(start.ToUniversalTime());
        captureRequests[0].EndDateUtc.Should().Be(end.ToUniversalTime());

        var cartItem = service.GetCart().Items.Should().ContainSingle().Subject;
        cartItem.StartDate.Should().Be(start);
        cartItem.EndDate.Should().Be(end);
        cartItem.ProductImageUrl.Should().Be(product.FullImageUrl);
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
        api.Setup(a => a.DeleteHoldAsync(It.IsAny<Guid>())).ReturnsAsync(true);
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

        await service.AddToCartAsync(product, 1, DateTime.Today, DateTime.Today.AddDays(2));
        await service.EnsureHoldsAsync();
        var item = service.GetCart().Items.Single();
        var initialHold = item.HoldId;
        initialHold.Should().NotBeNull();

        // Act
        var newStart = DateTime.Today.AddDays(5);
        var newEnd = newStart.AddDays(2);
        await service.UpdateDatesAsync(product.Id, newStart, newEnd);

        // Assert
        api.Verify(a => a.DeleteHoldAsync(initialHold!.Value), Times.Once);
        item.StartDate.Should().Be(newStart);
        item.EndDate.Should().Be(newEnd);
        item.HoldId.Should().NotBeNull();
        item.HoldId!.Value.Should().NotBe(initialHold!.Value);
    }

    [Fact]
    public async Task ValidateAvailabilityAsync_ReturnsFalseWhenStockInsufficient()
    {
        // Arrange
        var js = CreateJsRuntimeMock();
        var api = new Mock<IApiService>();
        var productId = Guid.NewGuid();

        api.SetupSequence(a => a.GetProductsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ProductDto>
            {
                new()
                {
                    Id = productId,
                    Name = "Kayak",
                    IsAvailable = true,
                    AvailableQuantity = 5
                }
            })
            .ReturnsAsync(new List<ProductDto>
            {
                new()
                {
                    Id = productId,
                    Name = "Kayak",
                    IsAvailable = false,
                    AvailableQuantity = 0
                }
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
        api.Setup(a => a.GetProductsAsync(It.IsAny<int>(), It.IsAny<int>()))
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
