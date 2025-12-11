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

// Ładuj konfigurację dla środowiska (Production/Development)
var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
using var response = await http.GetAsync($"appsettings.{builder.HostEnvironment.Environment}.json");
if (response.IsSuccessStatusCode)
{
    using var stream = await response.Content.ReadAsStreamAsync();
    builder.Configuration.AddJsonStream(stream);
}

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

// Automatyczne wykrywanie API URL na podstawie środowiska
var baseUrl = configuration["Api:BaseUrl"];
var hostAddress = builder.HostEnvironment.BaseAddress;

// Produkcja: Azure Static Web Apps -> Admin na Azure App Service
if (hostAddress.Contains("azurestaticapps.net") || hostAddress.Contains("nice-tree"))
{
    baseUrl = "https://sradmin2.azurewebsites.net";
}
// Development: użyj konfiguracji lub localhost Admin
else if (string.IsNullOrWhiteSpace(baseUrl))
{
    baseUrl = "http://localhost:5001";
}

apiService.SetBaseUrl(baseUrl);
Console.WriteLine($"🔗 API BaseUrl: {baseUrl}");

// Opcjonalnie: załaduj wybraną wypożyczalnię z LocalStorage (jeśli użytkownik wybrał)
var selectedTenantId = await tenantService.GetSelectedTenantIdAsync();
if (!string.IsNullOrEmpty(selectedTenantId) && Guid.TryParse(selectedTenantId, out var tenantId))
{
    apiService.SetTenantId(tenantId);
}
// NIE ustawiamy domyślnego tenant - użytkownik widzi wszystkie produkty ze wszystkich wypożyczalni

await host.RunAsync();


