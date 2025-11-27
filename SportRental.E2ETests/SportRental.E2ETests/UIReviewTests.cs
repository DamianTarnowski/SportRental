using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
public class UIReviewTests : BaseTest
{
    [Test]
    public async Task UI_Review_CompleteUserJourney()
    {
        Console.WriteLine("🎨 === PRZEGLĄD UI - KOMPLETNY JOURNEY UŻYTKOWNIKA ===\n");

        // 1. HOME PAGE
        Console.WriteLine("1️⃣ HOME PAGE");
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("ui_01_home");
        Console.WriteLine("   ✅ Screenshot: Home page\n");

        // 2. PRODUCTS - FULL VIEW
        Console.WriteLine("2️⃣ PRODUCTS - WIDOK PEŁNY");
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("ui_02_products_full");
        Console.WriteLine("   ✅ Screenshot: Products full view\n");

        // 3. PRODUCTS - SCROLL DOWN
        Console.WriteLine("3️⃣ PRODUCTS - PO SCROLLU");
        await Page.EvaluateAsync("window.scrollTo(0, 500)");
        await Task.Delay(500);
        await TakeScreenshotAsync("ui_03_products_scrolled");
        Console.WriteLine("   ✅ Screenshot: Products scrolled\n");

        // 4. PRODUCT CARD HOVER (jeśli możliwe)
        Console.WriteLine("4️⃣ PRODUCT CARD - INTERAKCJA");
        var firstCard = Page.Locator(".mud-card").First;
        if (await firstCard.CountAsync() > 0)
        {
            await firstCard.HoverAsync();
            await Task.Delay(500);
            await TakeScreenshotAsync("ui_04_product_card_hover");
            Console.WriteLine("   ✅ Screenshot: Product card hover\n");
        }

        // 5. PRODUCT DETAILS
        Console.WriteLine("5️⃣ PRODUCT DETAILS");
        var firstProductLink = Page.Locator("a[href^='/products/']").First;
        if (await firstProductLink.CountAsync() > 0)
        {
            await firstProductLink.ClickAsync();
            await WaitForPageLoadAsync();
            await Task.Delay(2000);
            await TakeScreenshotAsync("ui_05_product_details_top");
            Console.WriteLine("   ✅ Screenshot: Product details - top\n");

            // Scroll do sekcji akcji
            await Page.EvaluateAsync("window.scrollTo(0, 400)");
            await Task.Delay(500);
            await TakeScreenshotAsync("ui_06_product_details_actions");
            Console.WriteLine("   ✅ Screenshot: Product details - actions\n");

            // Scroll do related products
            await Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
            await Task.Delay(500);
            await TakeScreenshotAsync("ui_07_product_details_related");
            Console.WriteLine("   ✅ Screenshot: Product details - related\n");
        }

        // 6. DODAJ DO KOSZYKA
        Console.WriteLine("6️⃣ DODAWANIE DO KOSZYKA");
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        var addToCartButton = Page.Locator("button:has-text('Add to cart')").First;
        if (await addToCartButton.CountAsync() > 0)
        {
            await addToCartButton.ClickAsync();
            await Task.Delay(2000);
            await TakeScreenshotAsync("ui_08_after_add_to_cart");
            Console.WriteLine("   ✅ Screenshot: After adding to cart (snackbar?)\n");
        }

        // 7. CART - PEŁNY
        Console.WriteLine("7️⃣ CART - WIDOK PEŁNY");
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("ui_09_cart_full");
        Console.WriteLine("   ✅ Screenshot: Cart full view\n");

        // 8. CART - DATE PICKERS
        Console.WriteLine("8️⃣ CART - INTERAKCJA Z DATAMI");
        var datePicker = Page.Locator("input[type='text']").First;
        if (await datePicker.CountAsync() > 0)
        {
            await datePicker.ClickAsync();
            await Task.Delay(1000);
            await TakeScreenshotAsync("ui_10_cart_date_picker_open");
            Console.WriteLine("   ✅ Screenshot: Cart with date picker open\n");
            
            await Page.Keyboard.PressAsync("Escape");
            await Task.Delay(500);
        }

        // 9. CHECKOUT
        Console.WriteLine("9️⃣ CHECKOUT - TOP");
        await Page.GotoAsync($"{BaseUrl}/checkout");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("ui_11_checkout_top");
        Console.WriteLine("   ✅ Screenshot: Checkout - top\n");

        // 10. CHECKOUT - FORMULARZ
        Console.WriteLine("🔟 CHECKOUT - FORMULARZ");
        await Page.EvaluateAsync("window.scrollTo(0, 300)");
        await Task.Delay(500);
        await TakeScreenshotAsync("ui_12_checkout_form");
        Console.WriteLine("   ✅ Screenshot: Checkout - form\n");

        // 11. CONTACT
        Console.WriteLine("1️⃣1️⃣ CONTACT");
        await Page.GotoAsync($"{BaseUrl}/contact");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("ui_13_contact");
        Console.WriteLine("   ✅ Screenshot: Contact page\n");

        // 12. MY RENTALS (jeśli dostępne)
        Console.WriteLine("1️⃣2️⃣ MY RENTALS");
        await Page.GotoAsync($"{BaseUrl}/my-rentals");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("ui_14_my_rentals");
        Console.WriteLine("   ✅ Screenshot: My rentals\n");

        // 13. MOBILE VIEW - HOME
        Console.WriteLine("1️⃣3️⃣ MOBILE VIEW - HOME");
        await Page.SetViewportSizeAsync(375, 667); // iPhone SE
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("ui_15_mobile_home");
        Console.WriteLine("   ✅ Screenshot: Mobile - home\n");

        // 14. MOBILE VIEW - PRODUCTS
        Console.WriteLine("1️⃣4️⃣ MOBILE VIEW - PRODUCTS");
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("ui_16_mobile_products");
        Console.WriteLine("   ✅ Screenshot: Mobile - products\n");

        // 15. MOBILE VIEW - CART
        Console.WriteLine("1️⃣5️⃣ MOBILE VIEW - CART");
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("ui_17_mobile_cart");
        Console.WriteLine("   ✅ Screenshot: Mobile - cart\n");

        // 16. TABLET VIEW - PRODUCTS
        Console.WriteLine("1️⃣6️⃣ TABLET VIEW - PRODUCTS");
        await Page.SetViewportSizeAsync(768, 1024); // iPad
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("ui_18_tablet_products");
        Console.WriteLine("   ✅ Screenshot: Tablet - products\n");

        Console.WriteLine("\n📊 === ANALIZA ZAKOŃCZONA ===");
        Console.WriteLine("📸 Wszystkie screenshoty zapisane!");
    }

    [Test]
    public async Task UI_Review_EdgeCases()
    {
        Console.WriteLine("🔍 === PRZEGLĄD UI - EDGE CASES ===\n");

        // 1. EMPTY CART
        Console.WriteLine("1️⃣ PUSTY KOSZYK");
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("edge_01_empty_cart");
        Console.WriteLine("   ✅ Screenshot: Empty cart\n");

        // 2. EMPTY CHECKOUT
        Console.WriteLine("2️⃣ PUSTY CHECKOUT");
        await Page.GotoAsync($"{BaseUrl}/checkout");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("edge_02_empty_checkout");
        Console.WriteLine("   ✅ Screenshot: Empty checkout\n");

        // 3. 404 PAGE
        Console.WriteLine("3️⃣ 404 PAGE");
        await Page.GotoAsync($"{BaseUrl}/nie-istniejaca-strona");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("edge_03_404_page");
        Console.WriteLine("   ✅ Screenshot: 404 page\n");

        // 4. LONG PRODUCT NAME (jeśli jest)
        Console.WriteLine("4️⃣ TESTY RESPONSYWNOŚCI TEKSTU");
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        
        // Znajdź najdłuższą nazwę produktu
        var productNames = await Page.Locator(".mud-card-content h6, .mud-card-content .mud-typography-h6").AllTextContentsAsync();
        if (productNames.Count > 0)
        {
            var longestName = productNames.OrderByDescending(n => n.Length).FirstOrDefault();
            Console.WriteLine($"   Najdłuższa nazwa: {longestName}");
        }
        
        await TakeScreenshotAsync("edge_04_product_names");
        Console.WriteLine("   ✅ Screenshot: Product names\n");

        Console.WriteLine("\n📊 === ANALIZA EDGE CASES ZAKOŃCZONA ===");
    }

    [Test]
    public async Task UI_Review_Interactions()
    {
        Console.WriteLine("🖱️ === PRZEGLĄD UI - INTERAKCJE ===\n");

        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        // 1. TEST FILTRÓW
        Console.WriteLine("1️⃣ FILTRY KATEGORII");
        var categoryChips = Page.Locator(".mud-chip:has-text('Narty'), .mud-chip:has-text('Snowboard'), .mud-chip:has-text('Kitesurfing')");
        if (await categoryChips.CountAsync() > 0)
        {
            await TakeScreenshotAsync("interact_01_filters_before");
            Console.WriteLine("   ✅ Screenshot: Filters before click\n");

            await categoryChips.First.ClickAsync();
            await Task.Delay(1500);
            await TakeScreenshotAsync("interact_02_filters_after");
            Console.WriteLine("   ✅ Screenshot: Filters after click\n");
        }

        // 2. TEST PAGINACJI (jeśli jest)
        Console.WriteLine("2️⃣ PAGINACJA");
        var pagination = Page.Locator(".mud-pagination");
        if (await pagination.CountAsync() > 0)
        {
            await TakeScreenshotAsync("interact_03_pagination");
            Console.WriteLine("   ✅ Screenshot: Pagination\n");
        }

        // 3. TEST MENU MOBILNEGO
        Console.WriteLine("3️⃣ MENU MOBILNE");
        await Page.SetViewportSizeAsync(375, 667);
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        var menuButton = Page.Locator("button[aria-label*='menu'], button:has(svg)").First;
        if (await menuButton.CountAsync() > 0)
        {
            await TakeScreenshotAsync("interact_04_mobile_menu_closed");
            await menuButton.ClickAsync();
            await Task.Delay(1000);
            await TakeScreenshotAsync("interact_05_mobile_menu_open");
            Console.WriteLine("   ✅ Screenshot: Mobile menu\n");
        }

        Console.WriteLine("\n📊 === ANALIZA INTERAKCJI ZAKOŃCZONA ===");
    }
}

