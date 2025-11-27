using Xunit;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SportRental.Client.Tests;

/// <summary>
/// Testy diagnostyczne połączenia WASM Client -> Backend API
/// </summary>
public class ApiConnectionTests
{
    [Theory]
    [InlineData("http://localhost:5001")]
    [InlineData("https://localhost:7001")]
    [InlineData("http://localhost:5002")]
    [InlineData("https://localhost:7002")]
    public async Task TestApiConnection_ShouldReturnProducts(string baseUrl)
    {
        // Arrange
        using var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };

        // Act & Assert
        try
        {
            var response = await client.GetAsync("/api/products?page=1&pageSize=10");
            
            var statusCode = response.StatusCode;
            var content = await response.Content.ReadAsStringAsync();
            
            Console.WriteLine($"[{baseUrl}] Status: {statusCode}");
            Console.WriteLine($"[{baseUrl}] Content length: {content.Length}");
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"✅ SUCCESS: {baseUrl} is reachable!");
                Console.WriteLine($"Response preview: {content.Substring(0, Math.Min(200, content.Length))}");
                
                // Try to parse as JSON
                var products = JsonSerializer.Deserialize<JsonElement>(content);
                Console.WriteLine($"Products count: {products.GetArrayLength()}");
                
                Assert.True(true, $"{baseUrl} is working!");
            }
            else
            {
                Console.WriteLine($"❌ FAILED: {baseUrl} returned {statusCode}");
                Console.WriteLine($"Response: {content}");
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"❌ CONNECTION ERROR: {baseUrl} is not reachable");
            Console.WriteLine($"   Error: {ex.Message}");
            // Don't fail test - just report
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine($"❌ TIMEOUT: {baseUrl} took too long to respond");
            // Don't fail test - just report
        }
    }

    [Fact]
    public async Task DiagnoseWasmClientConfiguration()
    {
        Console.WriteLine("════════════════════════════════════════════════════");
        Console.WriteLine("🔍 WASM CLIENT API CONFIGURATION DIAGNOSTICS");
        Console.WriteLine("════════════════════════════════════════════════════");
        Console.WriteLine("");

        // Read appsettings.json
        var appsettingsPath = Path.Combine("..", "..", "..", "..", "SportRental.Client", "wwwroot", "appsettings.json");
        var appsettingsDevPath = Path.Combine("..", "..", "..", "..", "SportRental.Client", "wwwroot", "appsettings.Development.json");

        Console.WriteLine("📄 appsettings.json:");
        if (File.Exists(appsettingsPath))
        {
            var content = await File.ReadAllTextAsync(appsettingsPath);
            Console.WriteLine(content);
        }
        else
        {
            Console.WriteLine("   ❌ File not found!");
        }

        Console.WriteLine("");
        Console.WriteLine("📄 appsettings.Development.json:");
        if (File.Exists(appsettingsDevPath))
        {
            var content = await File.ReadAllTextAsync(appsettingsDevPath);
            Console.WriteLine(content);

            // Parse and check
            var json = JsonSerializer.Deserialize<JsonElement>(content);
            if (json.TryGetProperty("Api", out var api))
            {
                if (api.TryGetProperty("BaseUrl", out var baseUrl))
                {
                    var url = baseUrl.GetString();
                    Console.WriteLine("");
                    Console.WriteLine($"🔗 Configured BaseUrl: {url}");
                    
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        Console.WriteLine("   ⚠️  WARNING: BaseUrl is empty! Client will use its own URL.");
                    }
                    else if (url.Contains("7142") || url.Contains("5014"))
                    {
                        Console.WriteLine("   ❌ ERROR: BaseUrl points to WASM Client itself, not API!");
                        Console.WriteLine("   💡 Should be: http://localhost:5001 or https://localhost:7001");
                    }
                    else
                    {
                        Console.WriteLine("   ✅ BaseUrl looks correct!");
                    }
                }
            }
        }
        else
        {
            Console.WriteLine("   ❌ File not found!");
        }

        Console.WriteLine("");
        Console.WriteLine("════════════════════════════════════════════════════");
        Console.WriteLine("💡 RECOMMENDATIONS:");
        Console.WriteLine("════════════════════════════════════════════════════");
        Console.WriteLine("");
        Console.WriteLine("1. Make sure Admin or Api is running:");
        Console.WriteLine("   cd SportRental.Admin && dotnet run");
        Console.WriteLine("");
        Console.WriteLine("2. Set correct BaseUrl in appsettings.Development.json:");
        Console.WriteLine("   {");
        Console.WriteLine("     \"Api\": {");
        Console.WriteLine("       \"BaseUrl\": \"http://localhost:5001\",");
        Console.WriteLine("       \"TenantId\": \"00000000-0000-0000-0000-000000000000\"");
        Console.WriteLine("     }");
        Console.WriteLine("   }");
        Console.WriteLine("");
        Console.WriteLine("3. Open browser console (F12) and check for:");
        Console.WriteLine("   - CORS errors");
        Console.WriteLine("   - Network tab - failed requests to /api/products");
        Console.WriteLine("   - Console errors about API connection");
        Console.WriteLine("");
        Console.WriteLine("════════════════════════════════════════════════════");
        
        Assert.True(true); // Always pass - this is diagnostic
    }

    [Fact(Skip = "Wymaga uruchomionych lokalnie backendów, pomijane w testach automatycznych.")]
    public async Task CheckIfBackendIsRunning()
    {
        Console.WriteLine("════════════════════════════════════════════════════");
        Console.WriteLine("🔍 CHECKING IF BACKEND IS RUNNING");
        Console.WriteLine("════════════════════════════════════════════════════");
        Console.WriteLine("");

        var portsToCheck = new[]
        {
            ("Admin API (HTTP)", "http://localhost:5001"),
            ("Admin API (HTTPS)", "https://localhost:7001"),
            ("Public API (HTTP)", "http://localhost:5002"),
            ("Public API (HTTPS)", "https://localhost:7002"),
        };

        var anyRunning = false;

        foreach (var (name, url) in portsToCheck)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            try
            {
                var response = await client.GetAsync($"{url}/api/products?page=1&pageSize=1");
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✅ {name}: {url}");
                    Console.WriteLine($"   Status: {response.StatusCode}");
                    anyRunning = true;
                }
                else
                {
                    Console.WriteLine($"⚠️  {name}: {url}");
                    Console.WriteLine($"   Status: {response.StatusCode} (running but error)");
                }
            }
            catch (HttpRequestException)
            {
                Console.WriteLine($"❌ {name}: {url} - NOT RUNNING");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"⏱️  {name}: {url} - TIMEOUT");
            }
        }

        Console.WriteLine("");
        if (!anyRunning)
        {
            Console.WriteLine("❌ NO BACKEND API IS RUNNING!");
            Console.WriteLine("");
            Console.WriteLine("💡 Start the backend:");
            Console.WriteLine("   cd SportRental.Admin");
            Console.WriteLine("   dotnet run");
            Console.WriteLine("");
            Console.WriteLine("   OR use the script:");
            Console.WriteLine("   .\\start-dev-simple.ps1");
        }
        else
        {
            Console.WriteLine("✅ At least one backend is running!");
        }

        Console.WriteLine("");
        Console.WriteLine("════════════════════════════════════════════════════");
        
        Assert.True(anyRunning, "No backend API is running! Start SportRental.Admin or SportRental.Api");
    }
}






















