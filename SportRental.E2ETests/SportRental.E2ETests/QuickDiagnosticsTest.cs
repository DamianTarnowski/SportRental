using Microsoft.Playwright;
using NUnit.Framework;

namespace SportRental.E2ETests;

[TestFixture]
public class QuickDiagnosticsTest : BaseTest
{
    [Test]
    public async Task Diagnostics_CheckProductsWithConsoleLogging()
    {
        Console.WriteLine("\n🔍 DIAGNOSTYKA: Sprawdzam produkty z logami konsoli...\n");
        
        // Zbieraj logi konsoli
        var consoleLogs = new List<string>();
        Page.Console += (_, msg) =>
        {
            consoleLogs.Add($"[{msg.Type}] {msg.Text}");
            Console.WriteLine($"   CONSOLE: [{msg.Type}] {msg.Text}");
        };
        
        // Zbieraj błędy zapytań
        var failedRequests = new List<string>();
        Page.RequestFailed += (_, request) =>
        {
            failedRequests.Add($"{request.Method} {request.Url} - {request.Failure}");
            Console.WriteLine($"   ❌ REQUEST FAILED: {request.Method} {request.Url}");
            Console.WriteLine($"      Failure: {request.Failure}");
        };
        
        // Loguj wszystkie zapytania do API
        Page.Request += (_, request) =>
        {
            if (request.Url.Contains("/api/"))
            {
                Console.WriteLine($"   📡 API REQUEST: {request.Method} {request.Url}");
                
                // Loguj nagłówki
                var headers = request.Headers;
                if (headers.ContainsKey("x-tenant-id"))
                {
                    Console.WriteLine($"      X-Tenant-Id: {headers["x-tenant-id"]}");
                }
            }
        };
        
        // Loguj odpowiedzi API
        Page.Response += async (_, response) =>
        {
            if (response.Url.Contains("/api/"))
            {
                Console.WriteLine($"   ✅ API RESPONSE: {response.Status} {response.Url}");
                
                try
                {
                    var body = await response.TextAsync();
                    if (response.Url.Contains("/api/products"))
                    {
                        Console.WriteLine($"      Response body (first 500 chars): {body.Substring(0, Math.Min(500, body.Length))}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      Could not read response body: {ex.Message}");
                }
            }
        };
        
        // Idź do strony produktów
        Console.WriteLine("📄 Otwieram /products...\n");
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(5000); // Poczekaj 5 sekund na załadowanie
        
        await TakeScreenshotAsync("diagnostics_products");
        
        // Sprawdź ile produktów się załadowało
        var productCards = Page.Locator(".mud-card");
        var count = await productCards.CountAsync();
        
        Console.WriteLine($"\n📊 WYNIKI:");
        Console.WriteLine($"   Kart produktów: {count}");
        Console.WriteLine($"   Błędnych zapytań: {failedRequests.Count}");
        Console.WriteLine($"   Logów konsoli: {consoleLogs.Count}");
        
        if (failedRequests.Count > 0)
        {
            Console.WriteLine("\n❌ BŁĘDNE ZAPYTANIA:");
            foreach (var req in failedRequests)
            {
                Console.WriteLine($"   - {req}");
            }
        }
        
        if (consoleLogs.Count > 0)
        {
            Console.WriteLine("\n📋 OSTATNIE LOGI KONSOLI:");
            foreach (var log in consoleLogs.TakeLast(10))
            {
                Console.WriteLine($"   {log}");
            }
        }
        
        Console.WriteLine("\n✅ Test diagnostyczny zakończony");
    }
}


