using System.Net;
using System.Net.Http.Json;
using Blazored.LocalStorage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using SportRental.Client.Services;

namespace SportRental.Client.Tests.Services;

public class ApiAuthenticationStateProviderTests
{
    [Fact]
    public async Task GetAuthenticationState_ConcurrentCalls_SendsSingleRequest()
    {
        var handler = new CountingUnauthorizedHandler();
        var httpClient = new HttpClient(handler);
        var localStorage = new Mock<ILocalStorageService>();
        localStorage
            .Setup(storage => storage.GetItemAsync<string>("userId", It.IsAny<CancellationToken>()))
            .ReturnsAsync("known-user");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "https://app.example.test"
            })
            .Build();
        var sut = new ApiAuthenticationStateProvider(httpClient, localStorage.Object, configuration);

        var states = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => sut.GetAuthenticationStateAsync()));

        handler.RequestCount.Should().Be(2);
        states.Should().OnlyContain(state =>
            state.User.Identity == null || state.User.Identity.IsAuthenticated == false);
    }

    [Fact]
    public async Task GetAuthenticationState_WithoutLocalSessionMarker_StillChecksServer()
    {
        var handler = new CountingUnauthorizedHandler();
        var httpClient = new HttpClient(handler);
        var localStorage = new Mock<ILocalStorageService>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "https://app.example.test"
            })
            .Build();
        var sut = new ApiAuthenticationStateProvider(httpClient, localStorage.Object, configuration);

        var state = await sut.GetAuthenticationStateAsync();

        handler.RequestCount.Should().Be(2);
        handler.LastRequestUri.Should().Be("https://app.example.test/api/auth/refresh");
        Assert.False(state.User.Identity?.IsAuthenticated ?? false);
        localStorage.Verify(
            storage => storage.GetItemAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        localStorage.Verify(
            storage => storage.RemoveItemAsync("authToken", It.IsAny<CancellationToken>()),
            Times.Once);
        localStorage.Verify(
            storage => storage.RemoveItemAsync("refreshToken", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAuthenticationState_AuthenticatedResponse_AddsTenantAndCustomerClaims()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var handler = new AuthenticatedHandler(new
        {
            Id = "customer-user",
            Email = "klient@example.test",
            TenantId = tenantId,
            CustomerId = customerId,
            Roles = new[] { "Customer" }
        });
        var httpClient = new HttpClient(handler);
        var localStorage = new Mock<ILocalStorageService>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "https://app.example.test/"
            })
            .Build();
        var sut = new ApiAuthenticationStateProvider(httpClient, localStorage.Object, configuration);

        var state = await sut.GetAuthenticationStateAsync();

        state.User.Identity.Should().NotBeNull();
        state.User.Identity!.IsAuthenticated.Should().BeTrue();
        state.User.FindFirst("tenant-id")?.Value.Should().Be(tenantId.ToString());
        state.User.FindFirst("customer-id")?.Value.Should().Be(customerId.ToString());
        state.User.IsInRole("Customer").Should().BeTrue();
        handler.LastRequestUri.Should().Be("https://app.example.test/api/auth/me");
        localStorage.Verify(
            storage => storage.SetItemAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
        localStorage.Verify(
            storage => storage.RemoveItemAsync("userId", It.IsAny<CancellationToken>()),
            Times.Once);
        localStorage.Verify(
            storage => storage.RemoveItemAsync("userEmail", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAuthenticationState_WhenJwtExpired_RefreshesIdentitySessionAndRetriesMe()
    {
        var handler = new RefreshThenAuthenticatedHandler();
        var httpClient = new HttpClient(handler);
        var localStorage = new Mock<ILocalStorageService>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "https://app.example.test"
            })
            .Build();
        var sut = new ApiAuthenticationStateProvider(httpClient, localStorage.Object, configuration);

        var state = await sut.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeTrue();
        handler.Requests.Should().Equal(
            "GET /api/auth/me",
            "POST /api/auth/refresh",
            "GET /api/auth/me");
    }

    [Fact]
    public async Task GetAuthenticationState_TransientServerFailure_KeepsLastConfirmedIdentityInMemory()
    {
        var handler = new AuthenticatedThenUnavailableHandler();
        var httpClient = new HttpClient(handler);
        var localStorage = new Mock<ILocalStorageService>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "https://app.example.test"
            })
            .Build();
        var sut = new ApiAuthenticationStateProvider(httpClient, localStorage.Object, configuration);

        var confirmed = await sut.GetAuthenticationStateAsync();
        await sut.MarkUserAsAuthenticated();
        var duringFailure = await sut.GetAuthenticationStateAsync();

        confirmed.User.Identity!.IsAuthenticated.Should().BeTrue();
        duringFailure.User.Identity!.IsAuthenticated.Should().BeTrue();
        duringFailure.User.Identity.Name.Should().Be("klient@example.test");
        handler.RequestCount.Should().Be(2);
    }

    private sealed class CountingUnauthorizedHandler : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => _requestCount;
        public string? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            LastRequestUri = request.RequestUri?.ToString();
            await Task.Delay(50, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
    }

    private sealed class AuthenticatedHandler(object responseBody) : HttpMessageHandler
    {
        public string? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(responseBody)
            });
        }
    }

    private sealed class RefreshThenAuthenticatedHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (Requests.Count == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            if (Requests.Count == 2)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Id = "customer-user",
                    Email = "klient@example.test",
                    TenantId = (Guid?)null,
                    CustomerId = Guid.NewGuid(),
                    Roles = new[] { "Client" }
                })
            });
        }
    }

    private sealed class AuthenticatedThenUnavailableHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref _requestCount);
            if (requestNumber > 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Id = "customer-user",
                    Email = "klient@example.test",
                    TenantId = (Guid?)null,
                    CustomerId = Guid.NewGuid(),
                    Roles = new[] { "Client" }
                })
            });
        }
    }
}
