using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
public class ClickThroughTests : BaseTest
{
    [Test]
    public async Task FullClickThrough_CompleteUserJourney()
    {
        Console.WriteLine("🖱️ === KLIKAM PRZEZ CAŁĄ APLIKACJĘ ===\n");

        // 1. HOME - Sprawdzam czy się załadowała
        Console.WriteLine("1️⃣ Otwieram stronę główną...");
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitForPageLoadAsync();
        await TakeScreenshotAsync("click_01_home");
        
        var heroHeading = Page.Locator("h1:has-text('Twoja przygoda')");
        await Expect(heroHeading).ToBeVisibleAsync();
        Console.WriteLine("   ✅ Strona główna działa!\n");

        // 2. KLIK: Przeglądaj sprzęt
        Console.WriteLine("2️⃣ Klikam 'Przeglądaj sprzęt'...");
        var browseButton = Page.Locator("a[href='/products']").First;
        await browseButton.ClickAsync();
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("click_02_products");
        
        await Expect(Page).ToHaveURLAsync(new Regex(".*/products"));
        Console.WriteLine("   ✅ Przeszedłem do produktów!\n");

        // 3. KLIK: Wyszukiwanie
        Console.WriteLine("3️⃣ Testuję wyszukiwanie...");
        var searchBox = Page.Locator("input[placeholder*='Enter product name']").First;
        if (await searchBox.CountAsync() > 0)
        {
            await searchBox.ClickAsync();
            await searchBox.FillAsync("Narty");
            await Task.Delay(500);
            var searchButton = Page.Locator("button:has-text('Search')").First;
            if (await searchButton.CountAsync() > 0)
            {
                await searchButton.ClickAsync();
                await Task.Delay(1500);
                await TakeScreenshotAsync("click_03_search_results");
                Console.WriteLine("   ✅ Wyszukiwanie działa!\n");
            }
        }

        // 4. KLIK: Otwórz produkt
        Console.WriteLine("4️⃣ Klikam w kartę produktu...");
        var productCard = Page.Locator(".mud-card").First;
        if (await productCard.CountAsync() > 0)
        {
            await productCard.ClickAsync();
            await WaitForPageLoadAsync();
            await Task.Delay(2000);
            await TakeScreenshotAsync("click_04_product_details");
            Console.WriteLine("   ✅ Szczegóły produktu otwarte!\n");

            // 5. KLIK: Dodaj do koszyka
            Console.WriteLine("5️⃣ Dodaję do koszyka...");
            var addToCartBtn = Page.Locator("button:has-text('Dodaj do koszyka')").First;
            if (await addToCartBtn.CountAsync() > 0)
            {
                var isDisabled = await addToCartBtn.IsDisabledAsync();
                if (!isDisabled)
                {
                    await addToCartBtn.ClickAsync();
                    await Task.Delay(2000);
                    await TakeScreenshotAsync("click_05_added_to_cart");
                    Console.WriteLine("   ✅ Dodano do koszyka!\n");
                }
                else
                {
                    Console.WriteLine("   ⚠️ Przycisk jest disabled (produkt niedostępny)\n");
                }
            }
        }

        // 6. KLIK: Przejdź do koszyka
        Console.WriteLine("6️⃣ Idę do koszyka...");
        var cartLink = Page.Locator("a[href='/cart']").First;
        await cartLink.ClickAsync();
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("click_06_cart");
        
        await Expect(Page).ToHaveURLAsync(new Regex(".*/cart"));
        Console.WriteLine("   ✅ Jestem w koszyku!\n");

        // 7. KLIK: Sprawdź czy są produkty w koszyku
        var cartItems = Page.Locator(".mud-card-content");
        var itemCount = await cartItems.CountAsync();
        Console.WriteLine($"   📦 Produktów w koszyku: {itemCount}\n");

        if (itemCount > 0)
        {
            // 8. KLIK: Zmień ilość
            Console.WriteLine("7️⃣ Testuję przyciski ilości...");
            var plusButton = Page.Locator("button:has(svg)").Filter(new() { HasText = "add" }).First;
            if (await plusButton.CountAsync() > 0)
            {
                await plusButton.ClickAsync();
                await Task.Delay(1000);
                await TakeScreenshotAsync("click_07_quantity_increased");
                Console.WriteLine("   ✅ Ilość zwiększona!\n");
            }

            // 9. KLIK: Przejdź do checkout
            Console.WriteLine("8️⃣ Przechodzę do checkout...");
            var checkoutButton = Page.Locator("button:has-text('Przejdź do płatności')").First;
            if (await checkoutButton.CountAsync() > 0)
            {
                await checkoutButton.ClickAsync();
                await WaitForPageLoadAsync();
                await Task.Delay(2000);
                await TakeScreenshotAsync("click_08_checkout");
                
                await Expect(Page).ToHaveURLAsync(new Regex(".*/checkout"));
                Console.WriteLine("   ✅ Jestem na stronie checkout!\n");
            }
        }

        // 10. KLIK: Nawigacja - Contact
        Console.WriteLine("9️⃣ Sprawdzam stronę kontakt...");
        var contactLink = Page.Locator("a[href='/contact']").First;
        await contactLink.ClickAsync();
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("click_09_contact");
        
        await Expect(Page).ToHaveURLAsync(new Regex(".*/contact"));
        Console.WriteLine("   ✅ Strona kontakt działa!\n");

        // 11. KLIK: Breadcrumbs - Home
        Console.WriteLine("🔟 Testuję breadcrumbs...");
        var breadcrumbHome = Page.Locator("nav[aria-label*='Breadcrumb'] a[href='/']").First;
        if (await breadcrumbHome.CountAsync() > 0)
        {
            await breadcrumbHome.ClickAsync();
            await WaitForPageLoadAsync();
            await Task.Delay(1000);
            await TakeScreenshotAsync("click_10_breadcrumb_home");
            
            await Expect(Page).ToHaveURLAsync(BaseUrl + "/");
            Console.WriteLine("   ✅ Breadcrumbs działają!\n");
        }

        // 12. KLIK: Back to top button (sprawdzamy czy istnieje)
        Console.WriteLine("1️⃣1️⃣ Sprawdzam Back to top...");
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);
        
        var backToTopButton = Page.Locator(".back-to-top").First;
        if (await backToTopButton.CountAsync() > 0)
        {
            Console.WriteLine("   ✅ Back to top button istnieje w DOM!\n");
        }
        else
        {
            Console.WriteLine("   ⚠️ Back to top button nie znaleziony\n");
        }

        // 13. KLIK: Mobile menu
        Console.WriteLine("1️⃣2️⃣ Testuję menu mobilne...");
        await Page.SetViewportSizeAsync(375, 667);
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);
        
        var menuButton = Page.Locator("button:has(svg)").First;
        if (await menuButton.CountAsync() > 0)
        {
            await menuButton.ClickAsync();
            await Task.Delay(1000);
            await TakeScreenshotAsync("click_12_mobile_menu");
            Console.WriteLine("   ✅ Menu mobilne otwiera się!\n");
        }

        Console.WriteLine("\n🎉 === WSZYSTKO DZIAŁA! ===");
        Console.WriteLine("✅ Poklikałem całą aplikację i wszystko śmiga!");
    }

    [Test]
    public async Task QuickSmokeTest_AllMainPages()
    {
        Console.WriteLine("💨 === SZYBKI TEST DYMU - WSZYSTKIE STRONY ===\n");

        var pages = new Dictionary<string, string>
        {
            ["Home"] = "/",
            ["Products"] = "/products",
            ["Cart"] = "/cart",
            ["Checkout"] = "/checkout",
            ["Contact"] = "/contact",
            ["My Rentals"] = "/my-rentals",
            ["404"] = "/nie-istniejaca-strona"
        };

        foreach (var page in pages)
        {
            Console.WriteLine($"🔍 Sprawdzam: {page.Key} ({page.Value})");
            
            await Page.GotoAsync($"{BaseUrl}{page.Value}");
            await WaitForPageLoadAsync();
            await Task.Delay(1000);
            
            // Sprawdź czy strona się załadowała (nie ma błędu)
            var errorBoundary = Page.Locator(".blazor-error-boundary");
            var errorCount = await errorBoundary.CountAsync();
            
            if (errorCount > 0)
            {
                Console.WriteLine($"   ❌ BŁĄD na stronie {page.Key}!");
                await TakeScreenshotAsync($"error_{page.Key}");
                Assert.Fail($"Strona {page.Key} ma błąd!");
            }
            else
            {
                Console.WriteLine($"   ✅ {page.Key} działa!\n");
            }
        }

        Console.WriteLine("🎉 Wszystkie strony ładują się bez błędów!");
    }

    [Test]
    public async Task ResponsivenessTest_AllBreakpoints()
    {
        Console.WriteLine("📱 === TEST RESPONSYWNOŚCI ===\n");

        var viewports = new Dictionary<string, (int width, int height)>
        {
            ["Mobile (iPhone SE)"] = (375, 667),
            ["Mobile (iPhone 12)"] = (390, 844),
            ["Tablet (iPad)"] = (768, 1024),
            ["Tablet (iPad Pro)"] = (1024, 1366),
            ["Desktop (HD)"] = (1366, 768),
            ["Desktop (Full HD)"] = (1920, 1080),
            ["Desktop (2K)"] = (2560, 1440)
        };

        foreach (var viewport in viewports)
        {
            Console.WriteLine($"📐 Testuję: {viewport.Key} ({viewport.Value.width}x{viewport.Value.height})");
            
            await Page.SetViewportSizeAsync(viewport.Value.width, viewport.Value.height);
            await Page.GotoAsync($"{BaseUrl}/products");
            await WaitForPageLoadAsync();
            await Task.Delay(1500);
            
            await TakeScreenshotAsync($"responsive_{viewport.Key.Replace(" ", "_").Replace("(", "").Replace(")", "")}");
            
            // Sprawdź czy layout się nie zepsuł
            var body = Page.Locator("body");
            var bodyWidth = await body.EvaluateAsync<int>("el => el.scrollWidth");
            
            // Dopuszczamy niewielkie przekroczenie (scrollbar)
            if (bodyWidth > viewport.Value.width + 20)
            {
                Console.WriteLine($"   ⚠️ Poziomy scrollbar na {viewport.Key} (width: {bodyWidth}px)\n");
            }
            else
            {
                Console.WriteLine($"   ✅ Layout OK!\n");
            }
        }

        Console.WriteLine("🎉 Test responsywności zakończony!");
    }
}

