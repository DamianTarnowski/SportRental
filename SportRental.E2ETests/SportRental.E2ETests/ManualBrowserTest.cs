using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SportRental.E2ETests;

[TestFixture]
public class ManualBrowserTest : BaseTest
{
    [Test]
    public async Task Manual_OpenBrowser_Products()
    {
        Console.WriteLine("🌐 === OTWIERANIE PRZEGLĄDARKI - MANUAL TEST ===\n");
        Console.WriteLine("Test będzie działał przez 60 sekund żebyś mógł sprawdzić Network tab i Console.");
        
        var consoleMessages = new List<string>();
        var networkRequests = new List<(string method, string url, int? status)>();
        var errors = new List<string>();

        Page.Console += (_, msg) =>
        {
            var text = $"[{msg.Type}] {msg.Text}";
            consoleMessages.Add(text);
            Console.WriteLine($"📝 Console: {text}");
        };

        Page.Request += (_, request) =>
        {
            if (request.Url.Contains("/api/") || request.Url.Contains("appsettings"))
            {
                Console.WriteLine($"📤 Request: {request.Method} {request.Url}");
            }
        };

        Page.Response += (_, response) =>
        {
            if (response.Url.Contains("/api/") || response.Url.Contains("appsettings"))
            {
                networkRequests.Add((response.Request.Method, response.Url, response.Status));
                Console.WriteLine($"📥 Response: {response.Status} {response.Request.Method} {response.Url}");
            }
        };

        Page.PageError += (_, error) =>
        {
            errors.Add(error);
            Console.WriteLine($"❌ Page Error: {error}");
        };

        await Page.GotoAsync($"{BaseUrl}/products");
        Console.WriteLine($"\n✅ Opened: {BaseUrl}/products");
        Console.WriteLine($"Waiting 60 seconds...\n");

        // Wait and periodically check state
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(5000);
            
            try
            {
                var productCount = await Page.Locator(".mud-card").CountAsync();
                var totalChip = await Page.Locator("text=/Total: \\d+/").TextContentAsync();
                Console.WriteLine($"[{i*5}s] Product cards: {productCount}, Header: {totalChip}");
            }
            catch { }
        }

        Console.WriteLine($"\n📊 PODSUMOWANIE:");
        Console.WriteLine($"  Console messages: {consoleMessages.Count}");
        Console.WriteLine($"  Network requests (API): {networkRequests.Count}");
        Console.WriteLine($"  Errors: {errors.Count}");

        if (networkRequests.Any())
        {
            Console.WriteLine($"\n📡 API Calls:");
            foreach (var (method, url, status) in networkRequests)
            {
                Console.WriteLine($"  {status} {method} {url}");
            }
        }

        if (errors.Any())
        {
            Console.WriteLine($"\n❌ Errors:");
            foreach (var error in errors)
            {
                Console.WriteLine($"  {error}");
            }
        }

        await TakeScreenshotAsync("manual_browser_test");
    }
}

