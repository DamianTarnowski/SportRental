using SportRental.Admin.Services.Email;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace SportRental.Admin.Tests.Services.Email;

public class RentalReminderServiceSimpleTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<IServiceScope> _scopeMock;
    private readonly Mock<ILogger<RentalReminderService>> _loggerMock;
    private readonly RentalReminderService _reminderService;

    public RentalReminderServiceSimpleTests()
    {
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _scopeMock = new Mock<IServiceScope>();
        _loggerMock = new Mock<ILogger<RentalReminderService>>();

        // Minimal setup: CreateScope returns a disposable scope; ServiceProvider not used in these simple tests
        _scopeFactoryMock.Setup(x => x.CreateScope()).Returns(_scopeMock.Object);

        _reminderService = new RentalReminderService(_scopeFactoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldInitialize()
    {
        // Act & Assert
        _reminderService.Should().NotBeNull();
        _reminderService.Should().BeAssignableTo<Microsoft.Extensions.Hosting.IHostedService>();
    }

    [Fact]
    public async Task StartAsync_ShouldReturnCompletedTask()
    {
        // Arrange
        var cancellationToken = new CancellationToken();

        // Act
        var result = _reminderService.StartAsync(cancellationToken);

        // Assert
        await result;
        result.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_ShouldReturnCompletedTask()
    {
        // Arrange
        var cancellationToken = new CancellationToken();

        // Act
        var result = _reminderService.StopAsync(cancellationToken);

        // Assert  
        await result;
        result.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void Service_ShouldImplementIDisposable()
    {
        // Act & Assert
        _reminderService.Should().BeAssignableTo<IDisposable>();
    }
}