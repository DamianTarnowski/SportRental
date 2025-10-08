using System;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Blazored.LocalStorage;
using SportRental.Client;
using SportRental.Client.Services;
using SportRental.Shared.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Konfiguracja HttpClient
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Blazored LocalStorage
builder.Services.AddBlazoredLocalStorage();

// Authentication
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, ApiAuthenticationStateProvider>();
builder.Services.AddScoped<AuthService>();

// Dodanie MudBlazor
builder.Services.AddMudServices();

// Dodanie naszych serwisów
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<IApiService, ApiService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<ICustomerSessionService, CustomerSessionService>();

// Konfiguracja API
var host = builder.Build();
var apiService = host.Services.GetRequiredService<IApiService>();
var configuration = host.Services.GetRequiredService<IConfiguration>();
var tenantService = host.Services.GetRequiredService<TenantService>();

var baseUrl = configuration["Api:BaseUrl"];
if (string.IsNullOrWhiteSpace(baseUrl))
{
    baseUrl = builder.HostEnvironment.BaseAddress;
}
apiService.SetBaseUrl(baseUrl);

// Try to get tenant from TenantService (LocalStorage)
var selectedTenantId = await tenantService.GetSelectedTenantIdAsync();
if (!string.IsNullOrEmpty(selectedTenantId) && Guid.TryParse(selectedTenantId, out var tenantId))
{
    apiService.SetTenantId(tenantId);
}

await host.RunAsync();


