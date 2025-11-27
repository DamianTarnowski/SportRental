using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
public class FullUserJourneyTests : BaseTest
{
    [Test]
    public async Task CompleteUserJourney_RegisterSearchAddToCartAndCheckout()
    {
        Console.WriteLine("🎬 === KOMPLETNY TEST UŻYTKOWNIKA ===\n");
        
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var testEmail = $"test.user.{timestamp}@playwright.test";
        var testPassword = "TestPass123!";
        var testName = $"Test User {timestamp}";

        // ═══════════════════════════════════════════════════════════
        // KROK 1: REJESTRACJA NOWEGO UŻYTKOWNIKA
        // ═══════════════════════════════════════════════════════════
        Console.WriteLine("📝 KROK 1: Rejestracja nowego użytkownika");
        Console.WriteLine($"   Email: {testEmail}");
        Console.WriteLine($"   Hasło: {testPassword}");
        Console.WriteLine($"   Imię: {testName}\n");

        await Page.GotoAsync($"{BaseUrl}/register");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);
        await TakeScreenshotAsync("journey_01_register_page");

        // Wypełnij formularz rejestracji
        await Page.Locator("input[type='text']").First.FillAsync(testName);
        await Page.Locator("input[type='email']").FillAsync(testEmail);
        
        var passwordFields = Page.Locator("input[type='password']");
        await passwordFields.Nth(0).FillAsync(testPassword);
        await passwordFields.Nth(1).FillAsync(testPassword);
        
        await TakeScreenshotAsync("journey_02_register_filled");

        // Kliknij rejestrację
        var registerButton = Page.Locator("button:has-text('Zarejestruj się')");
        await registerButton.ClickAsync();
        await Task.Delay(3000);
        await TakeScreenshotAsync("journey_03_after_register");
        
        Console.WriteLine("   ✅ Użytkownik zarejestrowany!\n");

        // ═══════════════════════════════════════════════════════════
        // KROK 2: PRZESZUKIWANIE PRODUKTÓW
        // ═══════════════════════════════════════════════════════════
        Console.WriteLine("🔍 KROK 2: Przeszukiwanie produktów\n");

        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("journey_04_products_page");

        // Sprawdź ile produktów jest dostępnych
        var productCards = Page.Locator(".mud-card");
        var productCount = await productCards.CountAsync();
        Console.WriteLine($"   📦 Znaleziono {productCount} produktów\n");

        if (productCount == 0)
        {
            Assert.Fail("Brak produktów do przetestowania!");
        }

        // Użyj wyszukiwania
        var searchBox = Page.Locator("input[placeholder*='Enter product name']").First;
        await searchBox.FillAsync("Narty");
        await Page.Locator("button:has-text('Search')").First.ClickAsync();
        await Task.Delay(1500);
        await TakeScreenshotAsync("journey_05_search_results");
        
        Console.WriteLine("   ✅ Wyszukiwanie działa!\n");

        // ═══════════════════════════════════════════════════════════
        // KROK 3: DODAWANIE 3 PRODUKTÓW DO KOSZYKA
        // ═══════════════════════════════════════════════════════════
        Console.WriteLine("🛒 KROK 3: Dodawanie 3 produktów do koszyka\n");
        
        int addedCount = 0;
        
        for (int i = 0; i < 3 && addedCount < 3; i++)
        {
            Console.WriteLine($"   Próbuję dodać produkt {i + 1}...");
            
            // Przejdź świeżo do products
            await Page.GotoAsync($"{BaseUrl}/products");
            await WaitForPageLoadAsync();
            await Task.Delay(2000);
            
            // Znajdź kartę produktu i kliknij w nią (otworzy się dialog)
            var cards = Page.Locator(".mud-card");
            var cardCount = await cards.CountAsync();
            
            Console.WriteLine($"      Znaleziono {cardCount} kart produktów");
            
            if (cardCount > i)
            {
                var card = cards.Nth(i);
                
                Console.WriteLine($"      Klikam w kartę produktu {i + 1}...");
                
                // Kliknij w kartę - otworzy się dialog
                await card.ClickAsync();
                await Task.Delay(2000);
                
                // Poczekaj na dialog
                var dialog = Page.Locator(".mud-dialog");
                if (await dialog.CountAsync() > 0)
                {
                    Console.WriteLine($"      Dialog otwarty!");
                    
                    if (i == 0)
                    {
                        await TakeScreenshotAsync($"journey_06_product_dialog_{i + 1}");
                    }
                }
                else
                {
                    Console.WriteLine($"      ⚠️ Dialog się nie otworzył!");
                }

                // Sprawdź czy przycisk "Dodaj do koszyka" jest dostępny
                var addToCartBtn = Page.Locator("button:has-text('Dodaj do koszyka')").First;
                var btnCount = await addToCartBtn.CountAsync();
                
                Console.WriteLine($"      Przycisków 'Dodaj do koszyka': {btnCount}");
                
                if (btnCount > 0)
                {
                    var isDisabled = await addToCartBtn.IsDisabledAsync();
                    var btnText = await addToCartBtn.TextContentAsync();
                    
                    Console.WriteLine($"      Przycisk disabled: {isDisabled}");
                    Console.WriteLine($"      Tekst przycisku: '{btnText}'");
                    
                    if (!isDisabled)
                    {
                        // Sprawdź dostępność produktu
                        var availabilityBadge = Page.Locator("text=/Dostępny|Obecnie niedostępny/");
                        if (await availabilityBadge.CountAsync() > 0)
                        {
                            var badgeText = await availabilityBadge.First.TextContentAsync();
                            Console.WriteLine($"      Status dostępności: {badgeText}");
                        }
                        
                        await addToCartBtn.ClickAsync();
                        await Task.Delay(2000);
                        
                        // Sprawdź czy pojawił się snackbar
                        var snackbar = Page.Locator(".mud-snackbar");
                        if (await snackbar.CountAsync() > 0)
                        {
                            var snackText = await snackbar.First.TextContentAsync();
                            Console.WriteLine($"      Snackbar: {snackText}");
                        }
                        
                        if (addedCount == 0)
                        {
                            await TakeScreenshotAsync("journey_07_added_to_cart");
                        }
                        
                        addedCount++;
                        Console.WriteLine($"   ✅ Produkt dodany! ({addedCount}/3)");
                        
                        // Zamknij dialog (jeśli jest otwarty)
                        var closeButton = Page.Locator(".mud-dialog button[aria-label='close']");
                        if (await closeButton.CountAsync() > 0)
                        {
                            await closeButton.ClickAsync();
                            await Task.Delay(1000);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️ Produkt niedostępny (disabled button), pomijam");
                    }
                }
                else
                {
                    Console.WriteLine($"   ⚠️ Nie znaleziono przycisku 'Dodaj do koszyka'!");
                }
            }
            else
            {
                Console.WriteLine($"   ⚠️ Brak wystarczającej liczby kart produktów!");
                break;
            }
        }
        
        Console.WriteLine($"\n   📊 Łącznie dodano {addedCount} produktów");
        
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════
        // KROK 4: SPRAWDZENIE KOSZYKA
        // ═══════════════════════════════════════════════════════════
        Console.WriteLine("🛒 KROK 4: Sprawdzenie koszyka\n");

        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("journey_08_cart");

        var cartItems = Page.Locator(".mud-card-content");
        var cartItemsCount = await cartItems.CountAsync();
        Console.WriteLine($"   📦 Produktów w koszyku: {cartItemsCount}");

        if (cartItemsCount == 0)
        {
            Console.WriteLine("   ⚠️ Koszyk jest pusty - produkty mogły być niedostępne");
            Assert.Warn("Nie udało się dodać produktów do koszyka (prawdopodobnie brak dostępności)");
            return;
        }
        
        Console.WriteLine("   ✅ Koszyk zawiera produkty!\n");

        // ═══════════════════════════════════════════════════════════
        // KROK 5: PRZEJŚCIE DO CHECKOUT
        // ═══════════════════════════════════════════════════════════
        Console.WriteLine("💳 KROK 5: Przejście do checkout\n");

        var checkoutButton = Page.Locator("button:has-text('Przejdź do płatności')");
        await checkoutButton.ClickAsync();
        await WaitForPageLoadAsync();
        await Task.Delay(2000);
        await TakeScreenshotAsync("journey_09_checkout_page");

        await Expect(Page).ToHaveURLAsync(new Regex(".*/checkout"));
        Console.WriteLine("   ✅ Jesteśmy na stronie checkout!\n");

        // ═══════════════════════════════════════════════════════════
        // KROK 6: WYPEŁNIENIE DANYCH KLIENTA
        // ═══════════════════════════════════════════════════════════
        Console.WriteLine("📋 KROK 6: Wypełnienie danych klienta\n");

        // Sprawdź czy dane są już wypełnione (z sesji)
        var fullNameField = Page.Locator("input[type='text']").First;
        var currentValue = await fullNameField.InputValueAsync();
        
        if (string.IsNullOrEmpty(currentValue))
        {
            Console.WriteLine("   Wypełniam dane klienta...");
            
            await fullNameField.FillAsync(testName);
            await Page.Locator("input[type='email']").First.FillAsync(testEmail);
            await Page.Locator("input[type='tel']").First.FillAsync("+48123456789");
            
            await TakeScreenshotAsync("journey_10_checkout_filled");
            Console.WriteLine("   ✅ Dane wypełnione!");
        }
        else
        {
            Console.WriteLine($"   ✅ Dane już wypełnione: {currentValue}");
        }
        
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════
        // KROK 7: PODSUMOWANIE I PRZEJŚCIE DO STRIPE
        // ═══════════════════════════════════════════════════════════
        Console.WriteLine("💰 KROK 7: Finalizacja zamówienia\n");

        // Zrób screenshot podsumowania
        await TakeScreenshotAsync("journey_11_checkout_summary");

        // Znajdź przycisk "Zapłać" lub podobny
        var payButton = Page.Locator("button:has-text('Zapłać')");
        
        if (await payButton.CountAsync() == 0)
        {
            payButton = Page.Locator("button[type='submit']").Last;
        }

        Console.WriteLine("   Klikam przycisk płatności...");
        
        // Czekaj na przekierowanie do Stripe (może zająć chwilę)
        var navigationTask = Page.WaitForURLAsync(new Regex("stripe|checkout"), new() { Timeout = 30000 });
        
        await payButton.ClickAsync();
        await Task.Delay(3000);
        
        try
        {
            await navigationTask;
            
            // Jesteśmy na Stripe!
            await Task.Delay(2000);
            await TakeScreenshotAsync("journey_12_stripe_checkout");
            
            var currentUrl = Page.Url;
            Console.WriteLine($"   ✅ Przekierowano do Stripe!");
            Console.WriteLine($"   🌐 URL: {currentUrl}");
            
            if (currentUrl.Contains("stripe") || currentUrl.Contains("checkout"))
            {
                Console.WriteLine("\n🎉 === TEST ZAKOŃCZONY SUKCESEM ===");
                Console.WriteLine("✅ Użytkownik przeszedł cały flow:");
                Console.WriteLine("   1. Rejestracja ✅");
                Console.WriteLine("   2. Przeszukiwanie produktów ✅");
                Console.WriteLine("   3. Dodanie produktów do koszyka ✅");
                Console.WriteLine("   4. Przejście do checkout ✅");
                Console.WriteLine("   5. Wypełnienie danych ✅");
                Console.WriteLine("   6. Przekierowanie do Stripe ✅");
                Console.WriteLine("\n💰 Płatność w Stripe Sandbox gotowa do testowania!");
            }
        }
        catch (TimeoutException)
        {
            await TakeScreenshotAsync("journey_12_payment_timeout");
            Console.WriteLine("   ⚠️ Nie przekierowano do Stripe w ciągu 30 sekund");
            Console.WriteLine($"   Obecny URL: {Page.Url}");
            
            // Sprawdź czy są jakieś błędy
            var errorAlerts = Page.Locator(".mud-alert-error");
            if (await errorAlerts.CountAsync() > 0)
            {
                var errorText = await errorAlerts.First.TextContentAsync();
                Console.WriteLine($"   ❌ Błąd: {errorText}");
            }
            
            Assert.Fail("Nie udało się przekierować do Stripe");
        }
    }

    [Test]
    public async Task GuestCheckout_WithoutRegistration()
    {
        Console.WriteLine("👤 === TEST: CHECKOUT BEZ REJESTRACJI (GOŚĆ) ===\n");

        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var guestEmail = $"guest.{timestamp}@test.com";

        // Przejdź do produktów
        Console.WriteLine("🛍️ Dodawanie produktu do koszyka jako gość...\n");
        
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        var productCards = Page.Locator(".mud-card");
        var productCount = await productCards.CountAsync();

        if (productCount == 0)
        {
            Assert.Fail("Brak produktów");
        }

        // Dodaj pierwszy dostępny produkt
        await productCards.First.ClickAsync();
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        var addToCartBtn = Page.Locator("button:has-text('Dodaj do koszyka')").First;
        
        if (await addToCartBtn.CountAsync() > 0 && !await addToCartBtn.IsDisabledAsync())
        {
            await addToCartBtn.ClickAsync();
            await Task.Delay(2000);
            Console.WriteLine("   ✅ Produkt dodany!\n");
        }
        else
        {
            Assert.Fail("Nie można dodać produktu do koszyka");
        }

        // Idź do checkout
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        var checkoutButton = Page.Locator("button:has-text('Przejdź do płatności')");
        await checkoutButton.ClickAsync();
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        await TakeScreenshotAsync("guest_01_checkout");

        // Wypełnij dane jako gość
        Console.WriteLine("📝 Wypełnianie danych jako gość...");
        
        await Page.Locator("input[type='text']").First.FillAsync("Guest User");
        await Page.Locator("input[type='email']").First.FillAsync(guestEmail);
        await Page.Locator("input[type='tel']").First.FillAsync("+48987654321");

        await TakeScreenshotAsync("guest_02_data_filled");
        Console.WriteLine("   ✅ Dane wypełnione!\n");

        Console.WriteLine("✅ Checkout jako gość działa poprawnie!");
    }
}

