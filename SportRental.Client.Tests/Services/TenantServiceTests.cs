using Blazored.LocalStorage;
using FluentAssertions;
using Moq;
using SportRental.Client.Services;
using Xunit;

namespace SportRental.Client.Tests.Services;

/// <summary>
/// TenantService trzyma wybraną wypożyczalnię w LocalStorage. Logika prosta, ale krytyczna —
/// błąd w kluczach skutkuje gubieniem stanu między reloadami strony.
/// </summary>
public class TenantServiceTests
{
    private const string ExpectedTenantKey = "selected_tenant_id";
    private const string ExpectedTenantNameKey = "selected_tenant_name";

    [Fact]
    public async Task SetSelectedTenant_PersistsBothIdAndName()
    {
        var localStorage = new Mock<ILocalStorageService>();
        var sut = new TenantService(new HttpClient(), localStorage.Object);

        var tenantId = Guid.NewGuid().ToString();
        var tenantName = "Wypożyczalnia X";
        await sut.SetSelectedTenantAsync(tenantId, tenantName);

        localStorage.Verify(x => x.SetItemAsync(ExpectedTenantKey, tenantId, default), Times.Once);
        localStorage.Verify(x => x.SetItemAsync(ExpectedTenantNameKey, tenantName, default), Times.Once);
    }

    [Fact]
    public async Task GetSelectedTenantId_ReturnsValueFromStorage()
    {
        var expected = Guid.NewGuid().ToString();
        var localStorage = new Mock<ILocalStorageService>();
        localStorage.Setup(x => x.GetItemAsync<string>(ExpectedTenantKey, default))
                    .ReturnsAsync(expected);
        var sut = new TenantService(new HttpClient(), localStorage.Object);

        var actual = await sut.GetSelectedTenantIdAsync();

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task GetSelectedTenantId_NoTenantSelected_ReturnsNull()
    {
        var localStorage = new Mock<ILocalStorageService>();
        localStorage.Setup(x => x.GetItemAsync<string>(ExpectedTenantKey, default))
                    .ReturnsAsync((string?)null);
        var sut = new TenantService(new HttpClient(), localStorage.Object);

        var actual = await sut.GetSelectedTenantIdAsync();

        actual.Should().BeNull();
    }

    [Fact]
    public async Task GetSelectedTenantName_ReturnsValueFromStorage()
    {
        var localStorage = new Mock<ILocalStorageService>();
        localStorage.Setup(x => x.GetItemAsync<string>(ExpectedTenantNameKey, default))
                    .ReturnsAsync("Wypożyczalnia X");
        var sut = new TenantService(new HttpClient(), localStorage.Object);

        var actual = await sut.GetSelectedTenantNameAsync();

        actual.Should().Be("Wypożyczalnia X");
    }

    [Fact]
    public async Task ClearSelectedTenant_RemovesBothKeys()
    {
        var localStorage = new Mock<ILocalStorageService>();
        var sut = new TenantService(new HttpClient(), localStorage.Object);

        await sut.ClearSelectedTenantAsync();

        localStorage.Verify(x => x.RemoveItemAsync(ExpectedTenantKey, default), Times.Once);
        localStorage.Verify(x => x.RemoveItemAsync(ExpectedTenantNameKey, default), Times.Once);
    }

    [Fact]
    public async Task GetAvailableTenants_OnHttpFailure_PropagatesFailureToCallingPage()
    {
        var localStorage = new Mock<ILocalStorageService>();
        var failingHttp = new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new TenantService(failingHttp, localStorage.Object);

        var action = () => sut.GetAvailableTenantsAsync();

        await action.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("network down");
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("network down");
    }
}
