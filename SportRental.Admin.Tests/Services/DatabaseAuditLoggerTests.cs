using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using SportRental.Admin.Services.Logging;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;

namespace SportRental.Admin.Tests.Services;

public class DatabaseAuditLoggerTests
{
    private readonly Mock<IDbContextFactory<ApplicationDbContext>> _contextFactoryMock;
    private readonly Mock<ITenantProvider> _tenantProviderMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<ILogger<DatabaseAuditLogger>> _loggerMock;
    private readonly ApplicationDbContext _context;
    private readonly DatabaseAuditLogger _auditLogger;

    public DatabaseAuditLoggerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"test_audit_{Guid.NewGuid()}")
            .Options;
        _context = new ApplicationDbContext(options);

        _contextFactoryMock = new Mock<IDbContextFactory<ApplicationDbContext>>();
        // Zwracaj nowÄ… instancjÄ™ kontekstu dla loggera, aby nie utylizowaÄ‡ wspĂłĹ‚dzielonego _context
        _contextFactoryMock.Setup(x => x.CreateDbContext()).Returns(() => new ApplicationDbContext(options));

        _tenantProviderMock = new Mock<ITenantProvider>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _userManagerMock = CreateUserManagerMock();
        _loggerMock = new Mock<ILogger<DatabaseAuditLogger>>();

        _auditLogger = new DatabaseAuditLogger(
            _contextFactoryMock.Object,
            _tenantProviderMock.Object,
            _httpContextAccessorMock.Object,
            _userManagerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task LogAsync_WithValidTenant_ShouldCreateAuditLog()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);
        SetupHttpContext("testuser");

        // Act
        await _auditLogger.LogAsync(
            "Test audit message",
            "CREATE",
            "Product",
            entityId,
            "Info");

        // Assert
        var auditLogs = await _context.AuditLogs.ToListAsync();
        auditLogs.Should().HaveCount(1);
        
        var log = auditLogs.First();
        log.TenantId.Should().Be(tenantId);
        log.Message.Should().Be("Test audit message");
        log.Action.Should().Be("CREATE");
        log.EntityType.Should().Be("Product");
        log.EntityId.Should().Be(entityId);
        log.Level.Should().Be("Info");
        log.UserId.Should().Be("testuser");
        log.Date.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LogAsync_WithoutTenant_ShouldNotCreateAuditLog()
    {
        // Arrange
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns((Guid?)null);

        // Act
        await _auditLogger.LogAsync("Test message");

        // Assert
        var auditLogs = await _context.AuditLogs.ToListAsync();
        auditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task LogErrorAsync_WithValidTenant_ShouldCreateErrorLog()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var exception = new InvalidOperationException("Test exception");
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);
        SetupHttpContext("erroruser");

        // Act
        await _auditLogger.LogErrorAsync(
            "Test error message",
            exception,
            "TestSource",
            "Error");

        // Assert
        var errorLogs = await _context.ErrorLogs.ToListAsync();
        errorLogs.Should().HaveCount(1);
        
        var log = errorLogs.First();
        log.TenantId.Should().Be(tenantId);
        log.Message.Should().Be("Test error message");
        log.StackTrace.Should().Be(exception.StackTrace);
        log.Source.Should().Be("TestSource");
        log.Severity.Should().Be("Error");
        log.UserId.Should().Be("erroruser");
        log.Date.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LogErrorAsync_WithoutException_ShouldCreateErrorLogWithoutStackTrace()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);
        SetupHttpContext("warninguser");

        // Act
        await _auditLogger.LogErrorAsync(
            "Warning message",
            null,
            "TestSource",
            "Warning");

        // Assert
        var errorLogs = await _context.ErrorLogs.ToListAsync();
        errorLogs.Should().HaveCount(1);
        
        var log = errorLogs.First();
        log.Message.Should().Be("Warning message");
        log.StackTrace.Should().BeNull();
        log.Source.Should().Be("TestSource");
        log.Severity.Should().Be("Warning");
    }

    [Fact]
    public async Task LogUserActionAsync_ShouldCreateAuditLogWithCorrectParameters()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);
        SetupHttpContext("actionuser");

        // Act
        await _auditLogger.LogUserActionAsync(
            "UPDATE",
            "Customer",
            entityId,
            "User updated customer information");

        // Assert
        var auditLogs = await _context.AuditLogs.ToListAsync();
        auditLogs.Should().HaveCount(1);
        
        var log = auditLogs.First();
        log.Action.Should().Be("UPDATE");
        log.EntityType.Should().Be("Customer");
        log.EntityId.Should().Be(entityId);
        log.Message.Should().Be("User updated customer information");
        log.Level.Should().Be("Info");
        log.UserId.Should().Be("actionuser");
    }

    [Fact]
    public async Task LogAsync_WithUserGuid_ShouldSetUserGuidCorrectly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var userGuid = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);
        SetupHttpContext("guiduser");
        SetupUserManagerWithGuid("guiduser", userGuid);

        // Act
        await _auditLogger.LogAsync("Test with user guid");

        // Assert
        var auditLogs = await _context.AuditLogs.ToListAsync();
        auditLogs.Should().HaveCount(1);
        
        var log = auditLogs.First();
        log.UserId.Should().Be("guiduser");
        log.UserGuid.Should().Be(userGuid);
    }

    [Fact]
    public async Task LogAsync_WithInvalidUser_ShouldLogWithoutUserGuid()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);
        SetupHttpContext("invaliduser");
        _userManagerMock.Setup(x => x.GetUserAsync(It.Is<ClaimsPrincipal>(p => p.Identity != null && p.Identity.Name == "invaliduser")))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        await _auditLogger.LogAsync("Test with invalid user");

        // Assert
        var auditLogs = await _context.AuditLogs.ToListAsync();
        auditLogs.Should().HaveCount(1);
        
        var log = auditLogs.First();
        log.UserId.Should().Be("invaliduser");
        log.UserGuid.Should().BeNull();
    }

    [Fact]
    public async Task LogAsync_WithException_ShouldLogErrorAndNotThrow()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);
        _contextFactoryMock.Setup(x => x.CreateDbContext())
            .Throws(new InvalidOperationException("Database error"));

        // Act & Assert
        var act = async () => await _auditLogger.LogAsync("Test message");
        await act.Should().NotThrowAsync();

        // Verify error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to write audit log")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task LogErrorAsync_WithException_ShouldLogErrorAndNotThrow()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);
        _contextFactoryMock.Setup(x => x.CreateDbContext())
            .Throws(new InvalidOperationException("Database error"));

        // Act & Assert
        var act = async () => await _auditLogger.LogErrorAsync("Test error");
        await act.Should().NotThrowAsync();

        // Verify error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to write error log")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private void SetupHttpContext(string userName)
    {
        var httpContext = new Mock<HttpContext>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, userName)
        }));
        httpContext.Setup(x => x.User).Returns(user);
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext.Object);
    }

    private void SetupUserManagerWithGuid(string userName, Guid userGuid)
    {
        var user = new ApplicationUser { Id = userGuid, UserName = userName };
        _userManagerMock.Setup(x => x.GetUserAsync(It.Is<ClaimsPrincipal>(p => p.Identity != null && p.Identity.Name == userName)))
            .ReturnsAsync(user);
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null, null, null, null, null, null, null, null);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _context?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}





