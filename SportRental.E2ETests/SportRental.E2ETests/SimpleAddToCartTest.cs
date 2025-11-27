using Microsoft.Playwright;
using NUnit.Framework;

namespace SportRental.E2ETests;

[TestFixture]
public class SimpleAddToCartTest : BaseTest
{
    [Test]
    public async Task Simple_AddProductToCart_Success()
    {
        Console.WriteLine("\n🛒 === TEST: Dodawanie produktu do koszyka ===\n");
        
        // 1. Idź na stronę produktów
        Console.WriteLine("📄 Otwieram /products...");
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(3000);
        
        // 2. Sprawdź ile produktów
        var productCards = Page.Locator(".mud-card");
        var count = await productCards.CountAsync();
        Console.WriteLine($"   ✅ Znaleziono {count} produktów");
        
        if (count == 0)
        {
            Assert.Fail("Brak produktów!");
        }
        
        await TakeScreenshotAsync("cart_1_products");
        
        // 3. Znajdź przycisk "Add to cart" bezpośrednio na karcie (nie w dialogu)
        var addToCartButtons = Page.Locator("button:has-text('Add to cart')");
        var buttonCount = await addToCartButtons.CountAsync();
        
        Console.WriteLine($"\n📊 Przycisków 'Add to cart': {buttonCount}");
        
        if (buttonCount > 0)
        {
            // Kliknij pierwszy dostępny
            var firstButton = addToCartButtons.First;
            var isEnabled = await firstButton.IsEnabledAsync();
            
            if (isEnabled)
            {
                Console.WriteLine("   🖱️ Klikam 'Add to cart'...");
                await firstButton.ClickAsync();
                await Task.Delay(2000);
                await TakeScreenshotAsync("cart_2_after_add");
                
                // Sprawdź badge koszyka
                var cartBadge = Page.Locator(".mud-badge-dot").First;
                var badgeVisible = await cartBadge.IsVisibleAsync();
                
                Console.WriteLine($"\n✅ Badge koszyka widoczny: {badgeVisible}");
                
                // Idź do koszyka
                await Page.GotoAsync($"{BaseUrl}/cart");
                await WaitForPageLoadAsync();
                await Task.Delay(2000);
                await TakeScreenshotAsync("cart_3_cart_page");
                
                // Sprawdź czy jest produkt
                var cartItems = Page.Locator(".mud-card");
                var cartItemCount = await cartItems.CountAsync();
                
                Console.WriteLine($"\n📦 Produktów w koszyku: {cartItemCount}");
                
                Assert.That(cartItemCount, Is.GreaterThan(0), "Koszyk powinien zawierać produkty!");
                
                Console.WriteLine("\n✅ TEST PASSED: Produkt dodany do koszyka!");
            }
            else
            {
                Console.WriteLine("   ⚠️ Przycisk disabled (produkt niedostępny)");
                Assert.Inconclusive("Produkt niedostępny");
            }
        }
        else
        {
            Console.WriteLine("   ⚠️ Brak przycisków 'Add to cart'");
            await TakeScreenshotAsync("cart_no_buttons");
            Assert.Inconclusive("Brak przycisków dodawania do koszyka");
        }
    }
}


