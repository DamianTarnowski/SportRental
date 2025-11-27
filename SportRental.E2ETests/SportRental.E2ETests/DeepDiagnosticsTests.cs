using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SportRental.E2ETests;

[TestFixture]
public class DeepDiagnosticsTests : BaseTest
{
    [Test]
    public async Task DeepDiag_Products_PageContent()
    {
        Console.WriteLine("🔬 === GŁĘBOKA DIAGNOSTYKA STRONY PRODUKTÓW ===\n");

        // 1. Wejdź na stronę produktów
        await Page.GotoAsync($"{BaseUrl}/products");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(5000); // Czekaj 5 sekund na renderowanie Blazor

        Console.WriteLine("1️⃣ Sprawdzam tytuł strony:");
        var title = await Page.TitleAsync();
        Console.WriteLine($"   Title: {title}");

        Console.WriteLine("\n2️⃣ Sprawdzam czy header jest widoczny:");
        var header = await Page.Locator("text='Find your next adventure'").CountAsync();
        Console.WriteLine($"   Header visible: {header > 0}");

        Console.WriteLine("\n3️⃣ Sprawdzam liczniki w headerze:");
        try
        {
            var totalChip = await Page.Locator("text=/Total: \\d+/").TextContentAsync();
            var readyChip = await Page.Locator("text=/Ready: \\d+/").TextContentAsync();
            var avgPriceChip = await Page.Locator("text=/Avg price:/").TextContentAsync();
            Console.WriteLine($"   Total chip: {totalChip}");
            Console.WriteLine($"   Ready chip: {readyChip}");
            Console.WriteLine($"   Avg price chip: {avgPriceChip}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Nie można znaleźć chipów: {ex.Message}");
        }

        Console.WriteLine("\n4️⃣ Sprawdzam czy jest loader (skeleton):");
        var skeletons = await Page.Locator("[class*='mud-skeleton']").CountAsync();
        Console.WriteLine($"   Skeletons visible: {skeletons}");

        Console.WriteLine("\n5️⃣ Sprawdzam czy jest empty state:");
        var emptyState = await Page.Locator("text='No products match your filters'").CountAsync();
        Console.WriteLine($"   Empty state visible: {emptyState > 0}");

        Console.WriteLine("\n6️⃣ Sprawdzam różne selektory kart produktów:");
        var mudCards = await Page.Locator(".mud-card").CountAsync();
        var mudCardClass = await Page.Locator("[class*='mud-card']").CountAsync();
        var mudCardComponent = await Page.Locator("mud-card").CountAsync();
        var anyCard = await Page.Locator("div[class*='card']").CountAsync();
        Console.WriteLine($"   .mud-card: {mudCards}");
        Console.WriteLine($"   [class*='mud-card']: {mudCardClass}");
        Console.WriteLine($"   mud-card element: {mudCardComponent}");
        Console.WriteLine($"   div[class*='card']: {anyCard}");

        Console.WriteLine("\n7️⃣ Sprawdzam czy jest MudGrid:");
        var mudGrid = await Page.Locator("[class*='mud-grid']").CountAsync();
        Console.WriteLine($"   MudGrid count: {mudGrid}");

        Console.WriteLine("\n8️⃣ Sprawdzam wszystkie elementy w kontenerze:");
        try
        {
            var container = Page.Locator("[class*='mud-container']").First;
            var children = await container.Locator("> *").CountAsync();
            Console.WriteLine($"   Children in container: {children}");
            
            // Wypisz pierwsze 10 elementów
            for (int i = 0; i < Math.Min(children, 10); i++)
            {
                var child = container.Locator("> *").Nth(i);
                var className = await child.GetAttributeAsync("class");
                var tagName = await child.EvaluateAsync<string>("el => el.tagName");
                Console.WriteLine($"      [{i}] <{tagName}> class='{className?.Substring(0, Math.Min(50, className?.Length ?? 0))}'");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Nie można sprawdzić kontenera: {ex.Message}");
        }

        Console.WriteLine("\n9️⃣ Sprawdzam czy są obrazy produktów:");
        var productImages = await Page.Locator("img[alt]").CountAsync();
        Console.WriteLine($"   Images with alt: {productImages}");

        Console.WriteLine("\n🔟 Sprawdzam HTML całej strony (pierwsze 500 znaków):");
        var bodyHtml = await Page.Locator("body").InnerHTMLAsync();
        Console.WriteLine($"   Body HTML (preview): {bodyHtml.Substring(0, Math.Min(500, bodyHtml.Length))}...");

        Console.WriteLine("\n1️⃣1️⃣ Sprawdzam czy Blazor WebAssembly się załadował:");
        try
        {
            var blazorStarted = await Page.EvaluateAsync<bool>(@"
                () => {
                    return window.Blazor !== undefined && window.Blazor._internal !== undefined;
                }
            ");
            Console.WriteLine($"   Blazor started: {blazorStarted}");
        }
        catch
        {
            Console.WriteLine($"   Blazor started: false (error)");
        }

        await TakeScreenshotAsync("deep_diag_products");
    }

    [Test]
    public async Task DeepDiag_CheckAPICall()
    {
        Console.WriteLine("🌐 === DIAGNOSTYKA API CALL ===\n");

        var apiResponses = new List<(string url, int status, string body)>();

        Page.Response += async (_, response) =>
        {
            if (response.Url.Contains("/api/products"))
            {
                try
                {
                    var body = await response.TextAsync();
                    apiResponses.Add((response.Url, response.Status, body));
                    Console.WriteLine($"📥 API Response: {response.Status} {response.Url}");
                    Console.WriteLine($"   Body (first 200 chars): {body.Substring(0, Math.Min(200, body.Length))}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Error reading response: {ex.Message}");
                }
            }
        };

        await Page.GotoAsync($"{BaseUrl}/products");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(5000);

        Console.WriteLine($"\n📊 Total API responses: {apiResponses.Count}");
        
        if (apiResponses.Any())
        {
            var (url, status, body) = apiResponses.First();
            Console.WriteLine($"\nFirst API call details:");
            Console.WriteLine($"  URL: {url}");
            Console.WriteLine($"  Status: {status}");
            Console.WriteLine($"  Body length: {body.Length} chars");
            
            if (body.StartsWith("[") && body.Contains("id"))
            {
                var count = body.Count(c => c == '{');
                Console.WriteLine($"  Estimated product count: {count}");
            }
        }

        await TakeScreenshotAsync("deep_diag_api");
    }
}

