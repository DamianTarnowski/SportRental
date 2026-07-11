using System;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Blazored.LocalStorage;
using SportRental.Client;
using SportRental.Client.Services;
using SportRental.Shared.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = ApiBaseUrlResolver.Resolve(
    builder.HostEnvironment.BaseAddress,
    builder.Configuration["Api:BaseUrl"]);
builder.Configuration["Api:BaseUrl"] = apiBaseUrl;

// SEC-009: wszystkie fetch-e przez HttpClient muszą wysyłać credentials (HttpOnly cookie z JWT).
builder.Services.AddTransient<BrowserCredentialsHandler>();
builder.Services.AddHttpClient("SportRentalApi")
    .ConfigureHttpClient(client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<BrowserCredentialsHandler>();

// Wspólny klient ma BaseAddress dla serwisów używających relatywnych ścieżek.
// ApiService nadal buduje pełne URL-e, co HttpClient również poprawnie obsługuje.
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("SportRentalApi"));

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

apiService.SetBaseUrl(apiBaseUrl);
Console.WriteLine($"API BaseUrl: {apiBaseUrl}");

await host.RunAsync();


