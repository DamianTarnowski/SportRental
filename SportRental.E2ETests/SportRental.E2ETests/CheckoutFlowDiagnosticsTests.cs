using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace SportRental.E2ETests;

[TestFixture]
public class CheckoutFlowDiagnosticsTests : BaseTest
{
    [Test]
    public async Task Checkout_DiagnosticFlow()
    {
        Console.WriteLine("🛒 === DIAGNOSTYKA FLOW KOSZYKA → CHECKOUT ===\n");

        var consoleMessages = new List<string>();
        var errors = new List<string>();
        var apiCalls = new List<(string method, string url, int status, string? body)>();

        // Przechwytuj console
        Page.Console += (_, msg) =>
        {
            var text = $"[{msg.Type}] {msg.Text}";
            consoleMessages.Add(text);
            if (msg.Type == "error" || msg.Type == "warning")
            {
                Console.WriteLine($"⚠️ {text}");
            }
        };

        // Przechwytuj błędy strony
        Page.PageError += (_, error) =>
        {
            errors.Add(error);
            Console.WriteLine($"❌ Page Error: {error}");
        };

        // Przechwytuj API calls
        Page.Response += async (_, response) =>
        {
            if (response.Url.Contains("/api/"))
            {
                try
                {
                    var body = await response.TextAsync();
                    apiCalls.Add((response.Request.Method, response.Url, response.Status, body));
                    Console.WriteLine($"📡 {response.Status} {response.Request.Method} {response.Url}");
                    
                    if (response.Status >= 400)
                    {
                        Console.WriteLine($"   ❌ Error response: {body.Substring(0, Math.Min(200, body.Length))}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Couldn't read response: {ex.Message}");
                }
            }
        };

        // 1. Idź na stronę produktów
        Console.WriteLine("1️⃣ Przechodzę na stronę produktów...");
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(3000);

        // 2. Znajdź pierwszy produkt i dodaj do koszyka
        Console.WriteLine("\n2️⃣ Dodaję pierwszy produkt do koszyka...");
        var firstProduct = Page.Locator(".mud-card").First;
        
        if (await firstProduct.CountAsync() > 0)
        {
            await firstProduct.ClickAsync();
            await Task.Delay(2000);
            
            // Jeśli otworzył się dialog, zamknij go i kliknij "Add to cart" button
            var addToCartBtn = Page.Locator("button:has-text('Add to cart'), button:has-text('Dodaj do koszyka')").First;
            if (await addToCartBtn.CountAsync() > 0)
            {
                await addToCartBtn.ClickAsync();
                Console.WriteLine("   ✅ Kliknięto 'Add to cart'");
                await Task.Delay(2000);
            }
        }
        else
        {
            Console.WriteLine("   ⚠️ Nie znaleziono produktów!");
        }

        await TakeScreenshotAsync("checkout_1_products");

        // 3. Przejdź do koszyka
        Console.WriteLine("\n3️⃣ Przechodzę do koszyka...");
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        
        await TakeScreenshotAsync("checkout_2_cart");

        // Sprawdź czy są produkty w koszyku
        var cartItems = await Page.Locator(".mud-card, [class*='cart-item']").CountAsync();
        Console.WriteLine($"   Produktów w koszyku (karty): {cartItems}");

        // 4. Kliknij "Przejdź do płatności"
        Console.WriteLine("\n4️⃣ Klikam 'Przejdź do płatności'...");
        
        var checkoutButton = Page.Locator("button:has-text('Przejdź do płatności'), button:has-text('Proceed to checkout'), button:has-text('Checkout')");
        
        if (await checkoutButton.CountAsync() > 0)
        {
            Console.WriteLine($"   Znaleziono przycisk checkout (count: {await checkoutButton.CountAsync()})");
            
            try
            {
                await checkoutButton.First.ClickAsync();
                Console.WriteLine("   ✅ Kliknięto przycisk");
                await Task.Delay(3000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Błąd podczas klikania: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("   ⚠️ Nie znaleziono przycisku 'Przejdź do płatności'!");
        }

        await TakeScreenshotAsync("checkout_3_after_click");

        // 5. Sprawdź czy jesteśmy na stronie checkout
        var currentUrl = Page.Url;
        Console.WriteLine($"\n5️⃣ Obecny URL: {currentUrl}");
        
        if (currentUrl.Contains("/checkout"))
        {
            Console.WriteLine("   ✅ Przekierowano na /checkout");
        }
        else
        {
            Console.WriteLine("   ⚠️ NIE jesteśmy na /checkout!");
        }

        // 6. Sprawdź snackbary
        var snackbars = await Page.Locator(".mud-snackbar, [class*='snackbar']").CountAsync();
        Console.WriteLine($"\n6️⃣ Snackbars visible: {snackbars}");
        
        if (snackbars > 0)
        {
            for (int i = 0; i < snackbars; i++)
            {
                var snackbar = Page.Locator(".mud-snackbar, [class*='snackbar']").Nth(i);
                var text = await snackbar.TextContentAsync();
                var classes = await snackbar.GetAttributeAsync("class");
                Console.WriteLine($"   Snackbar {i}: {text}");
                Console.WriteLine($"   Classes: {classes}");
            }
        }

        await TakeScreenshotAsync("checkout_4_final");

        // PODSUMOWANIE
        Console.WriteLine($"\n📊 === PODSUMOWANIE ===");
        Console.WriteLine($"Console messages: {consoleMessages.Count}");
        Console.WriteLine($"Errors: {errors.Count}");
        Console.WriteLine($"API calls: {apiCalls.Count}");

        if (errors.Any())
        {
            Console.WriteLine($"\n❌ JS ERRORS:");
            foreach (var error in errors)
            {
                Console.WriteLine($"  {error}");
            }
        }

        if (apiCalls.Any(c => c.status >= 400))
        {
            Console.WriteLine($"\n❌ FAILED API CALLS:");
            foreach (var (method, url, status, body) in apiCalls.Where(c => c.status >= 400))
            {
                Console.WriteLine($"  {status} {method} {url}");
                if (body != null)
                {
                    Console.WriteLine($"    Body: {body.Substring(0, Math.Min(300, body.Length))}");
                }
            }
        }
    }
}

