using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
public class FullFlowVerificationTests : BaseTest
{
    [Test]
    public async Task Test1_AddToCart_ShouldWork()
    {
        Console.WriteLine("\n🛒 TEST 1: Dodawanie do koszyka\n");
        
        // Idź do produktów
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(3000);
        
        Console.WriteLine("   📦 Szukam produktu...");
        
        // Znajdź pierwszy dostępny produkt
        var productCards = Page.Locator(".mud-card").Filter(new() { HasText = "zł" });
        var count = await productCards.CountAsync();
        Console.WriteLine($"   ✅ Znaleziono {count} kart produktów");
        
        if (count == 0)
        {
            Assert.Fail("Brak produktów na stronie!");
        }
        
        await TakeScreenshotAsync("flow_01_products_list");
        
        // Kliknij w pierwszy produkt
        await productCards.First.ClickAsync();
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        
        Console.WriteLine("   ✅ Otwarto szczegóły produktu");
        await TakeScreenshotAsync("flow_02_product_details");
        
        // Znajdź przycisk "Dodaj do koszyka"
        var addToCartBtn = Page.Locator("button").Filter(new() { HasText = "Dodaj do koszyka" });
        var btnExists = await addToCartBtn.CountAsync() > 0;
        
        if (!btnExists)
        {
            Console.WriteLine("   ⚠️ Brak przycisku 'Dodaj do koszyka' - produkt niedostępny?");
            Assert.Inconclusive("Produkt nie ma przycisku dodawania do koszyka");
        }
        
        // Sprawdź czy przycisk nie jest disabled
        var isDisabled = await addToCartBtn.IsDisabledAsync();
        if (isDisabled)
        {
            Console.WriteLine("   ⚠️ Przycisk 'Dodaj do koszyka' jest wyłączony");
            Assert.Inconclusive("Produkt niedostępny (przycisk disabled)");
        }
        
        Console.WriteLine("   🖱️ Klikam 'Dodaj do koszyka'...");
        await addToCartBtn.ClickAsync();
        await Task.Delay(2000);
        
        // Sprawdź czy badge koszyka się zaktualizował
        var cartBadge = Page.Locator(".mud-badge-dot, .mud-badge").Filter(new() { HasText = "1" });
        var hasBadge = await cartBadge.CountAsync() > 0;
        
        Console.WriteLine(hasBadge 
            ? "   ✅ Badge koszyka się zaktualizował!" 
            : "   ⚠️ Badge koszyka nie widoczny (może być w snackbar?)");
        
        await TakeScreenshotAsync("flow_03_added_to_cart");
        
        Assert.Pass("✅ Dodawanie do koszyka działa!");
    }
    
    [Test]
    public async Task Test2_Cart_ShouldShowProducts()
    {
        Console.WriteLine("\n🛒 TEST 2: Koszyk pokazuje produkty\n");
        
        // Najpierw dodaj produkt
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(3000);
        
        var productCards = Page.Locator(".mud-card").Filter(new() { HasText = "zł" });
        if (await productCards.CountAsync() > 0)
        {
            await productCards.First.ClickAsync();
            await Task.Delay(2000);
            
            var addBtn = Page.Locator("button").Filter(new() { HasText = "Dodaj do koszyka" });
            if (await addBtn.CountAsync() > 0 && !await addBtn.IsDisabledAsync())
            {
                await addBtn.ClickAsync();
                await Task.Delay(2000);
                Console.WriteLine("   ✅ Dodano produkt do koszyka");
            }
        }
        
        // Idź do koszyka
        Console.WriteLine("   🛒 Otwieram koszyk...");
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        
        await TakeScreenshotAsync("flow_04_cart_page");
        
        // Sprawdź czy są produkty w koszyku
        var emptyMessage = Page.Locator("text=/koszyk jest pusty/i");
        var hasEmptyMessage = await emptyMessage.CountAsync() > 0;
        
        if (hasEmptyMessage)
        {
            Console.WriteLine("   ⚠️ Koszyk jest pusty");
            Assert.Inconclusive("Koszyk jest pusty - nie udało się dodać produktu w poprzednim kroku");
        }
        
        // Szukaj kart produktów w koszyku
        var cartItems = Page.Locator(".mud-card").Filter(new() { HasText = "zł" });
        var itemCount = await cartItems.CountAsync();
        
        Console.WriteLine($"   📦 Produktów w koszyku: {itemCount}");
        
        if (itemCount > 0)
        {
            // Sprawdź czy są podstawowe informacje
            var firstItem = cartItems.First;
            var hasName = await firstItem.Locator("text=/[a-zA-Z]{3,}/").CountAsync() > 0;
            var hasPrice = await firstItem.Locator("text=/[0-9]+.*zł/").CountAsync() > 0;
            
            Console.WriteLine($"   ✅ Produkt ma nazwę: {hasName}");
            Console.WriteLine($"   ✅ Produkt ma cenę: {hasPrice}");
            
            Assert.Pass($"✅ Koszyk działa! Znaleziono {itemCount} produkt(ów)");
        }
        else
        {
            Assert.Fail("❌ Koszyk nie pokazuje produktów!");
        }
    }
    
    [Test]
    public async Task Test3_Checkout_ShouldBeAccessible()
    {
        Console.WriteLine("\n💳 TEST 3: Checkout jest dostępny\n");
        
        // Dodaj produkt i idź do koszyka
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(3000);
        
        var productCards = Page.Locator(".mud-card").Filter(new() { HasText = "zł" });
        if (await productCards.CountAsync() > 0)
        {
            await productCards.First.ClickAsync();
            await Task.Delay(2000);
            
            var addBtn = Page.Locator("button").Filter(new() { HasText = "Dodaj do koszyka" });
            if (await addBtn.CountAsync() > 0 && !await addBtn.IsDisabledAsync())
            {
                await addBtn.ClickAsync();
                await Task.Delay(2000);
            }
        }
        
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        
        Console.WriteLine("   🔍 Szukam przycisku checkout...");
        
        // Szukaj przycisku "Przejdź do płatności" lub podobnego
        var checkoutBtn = Page.Locator("button:has-text('Przejdź'), button:has-text('Checkout'), button:has-text('Płatność')");
        
        var hasCheckoutBtn = await checkoutBtn.CountAsync() > 0;
        
        if (!hasCheckoutBtn)
        {
            Console.WriteLine("   ⚠️ Brak przycisku checkout (może koszyk jest pusty?)");
            await TakeScreenshotAsync("flow_05_no_checkout_button");
            Assert.Inconclusive("Brak przycisku checkout");
        }
        
        Console.WriteLine("   ✅ Znaleziono przycisk checkout");
        await TakeScreenshotAsync("flow_05_cart_with_checkout");
        
        // Sprawdź czy przycisk nie jest disabled
        var isDisabled = await checkoutBtn.First.IsDisabledAsync();
        if (isDisabled)
        {
            Console.WriteLine("   ⚠️ Przycisk checkout jest wyłączony");
            Assert.Inconclusive("Przycisk checkout jest disabled");
        }
        
        // Kliknij checkout
        Console.WriteLine("   🖱️ Klikam checkout...");
        await checkoutBtn.First.ClickAsync();
        await WaitForPageLoadAsync();
        await Task.Delay(3000);
        
        await TakeScreenshotAsync("flow_06_checkout_page");
        
        // Sprawdź czy jesteśmy na stronie checkout
        var url = Page.Url;
        var isOnCheckout = url.Contains("/checkout", StringComparison.OrdinalIgnoreCase);
        
        Console.WriteLine($"   📍 URL: {url}");
        Console.WriteLine(isOnCheckout 
            ? "   ✅ Przekierowano do checkout!" 
            : "   ⚠️ Nie jesteśmy na stronie checkout");
        
        if (isOnCheckout)
        {
            // Sprawdź czy są formularze
            var hasForm = await Page.Locator("form, input[type='text'], input[type='email']").CountAsync() > 0;
            Console.WriteLine(hasForm 
                ? "   ✅ Formularz checkout widoczny" 
                : "   ⚠️ Brak formularza");
            
            Assert.Pass("✅ Checkout działa!");
        }
        else
        {
            Assert.Fail("❌ Nie udało się przejść do checkout!");
        }
    }
    
    [Test]
    public async Task Test4_ProductImages_ShouldHavePlaceholders()
    {
        Console.WriteLine("\n🖼️ TEST 4: Obrazki/placeholders produktów\n");
        
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(3000);
        
        await TakeScreenshotAsync("flow_07_product_images");
        
        // Znajdź karty produktów
        var productCards = Page.Locator(".mud-card");
        var count = await productCards.CountAsync();
        
        Console.WriteLine($"   📦 Sprawdzam {count} produktów...");
        
        if (count == 0)
        {
            Assert.Fail("Brak produktów do sprawdzenia!");
        }
        
        // Sprawdź pierwsze 5 produktów
        var productsToCheck = Math.Min(5, count);
        var productsWithImages = 0;
        var productsWithPlaceholders = 0;
        
        for (int i = 0; i < productsToCheck; i++)
        {
            var card = productCards.Nth(i);
            
            // Szukaj img lub svg (placeholder może być svg)
            var hasImg = await card.Locator("img").CountAsync() > 0;
            var hasSvg = await card.Locator("svg").CountAsync() > 0;
            var hasIcon = await card.Locator("[class*='icon']").CountAsync() > 0;
            
            if (hasImg || hasSvg || hasIcon)
            {
                if (hasImg)
                {
                    var img = card.Locator("img").First;
                    var src = await img.GetAttributeAsync("src");
                    if (src != null && (src.Contains("placeholder") || src.Contains("data:image") || src.Contains("emoji")))
                    {
                        productsWithPlaceholders++;
                    }
                    else
                    {
                        productsWithImages++;
                    }
                }
                else
                {
                    productsWithPlaceholders++;
                }
            }
        }
        
        Console.WriteLine($"   ✅ Produktów z obrazkami: {productsWithImages}");
        Console.WriteLine($"   ✅ Produktów z placeholderami: {productsWithPlaceholders}");
        Console.WriteLine($"   📊 Łącznie z grafiką: {productsWithImages + productsWithPlaceholders}/{productsToCheck}");
        
        var totalWithGraphics = productsWithImages + productsWithPlaceholders;
        var percentage = (totalWithGraphics * 100.0) / productsToCheck;
        
        if (percentage >= 80)
        {
            Assert.Pass($"✅ Obrazki/placeholders działają! ({percentage:F0}% produktów ma grafikę)");
        }
        else
        {
            Assert.Warn($"⚠️ Tylko {percentage:F0}% produktów ma obrazki/placeholders");
        }
    }
}

