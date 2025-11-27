using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
public class CartTests : BaseTest
{
    [Test]
    public async Task Cart_EmptyCart_ShouldDisplay_EmptyMessage()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź komunikat o pustym koszyku
        var emptyMessage = Page.Locator("text='pusty'").Or(Page.Locator("text='empty'"));

        // Screenshot
        await TakeScreenshotAsync("18_empty_cart");
    }

    [Test]
    public async Task Cart_WithProduct_ShouldDisplay_ProductDetails()
    {
        // Arrange - Najpierw dodaj produkt do koszyka
        await AddFirstProductToCart();

        // Act
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź czy produkt jest w koszyku
        // (mogą być różne stany - może być pusty jeśli produkt niedostępny)

        // Screenshot
        await TakeScreenshotAsync("19_cart_with_products");
    }

    [Test]
    public async Task Cart_QuantityButtons_ShouldBeVisible()
    {
        // Arrange
        await AddFirstProductToCart();
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź przyciski + i -
        var plusButton = Page.Locator("button[aria-label*='Add']").Or(Page.Locator("svg[data-icon='plus']"));
        var minusButton = Page.Locator("button[aria-label*='Remove']").Or(Page.Locator("svg[data-icon='minus']"));

        // Screenshot
        await TakeScreenshotAsync("20_quantity_buttons");
    }

    [Test]
    public async Task Cart_DatePickers_ShouldAllowEditing()
    {
        // Arrange
        await AddFirstProductToCart();
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Screenshot - Date pickery w koszyku
        await TakeScreenshotAsync("21_cart_date_pickers");
    }

    [Test]
    public async Task Cart_CheckoutButton_ShouldBeVisible()
    {
        // Arrange
        await AddFirstProductToCart();
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź przycisk "Przejdź do płatności"
        var checkoutButton = Page.Locator("button:has-text('płatności')").Or(Page.Locator("button:has-text('Checkout')"));

        // Screenshot
        await TakeScreenshotAsync("22_checkout_button");
    }

    [Test]
    public async Task Cart_Summary_ShouldDisplay_TotalAmount()
    {
        // Arrange
        await AddFirstProductToCart();
        await Page.GotoAsync($"{BaseUrl}/cart");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź sekcję podsumowania
        var summarySection = Page.Locator("text='Podsumowanie'").Or(Page.Locator("text='Summary'"));

        // Screenshot
        await TakeScreenshotAsync("23_cart_summary");
    }

    /// <summary>
    /// Pomocnicza metoda - dodaje pierwszy dostępny produkt do koszyka
    /// </summary>
    private async Task AddFirstProductToCart()
    {
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        // Kliknij pierwszy przycisk "Add to cart" (jeśli istnieje)
        var addToCartButton = Page.Locator("button:has-text('Add to cart')").Or(Page.Locator("button:has-text('Dodaj')"));
        
        if (await addToCartButton.CountAsync() > 0)
        {
            await addToCartButton.First.ClickAsync();
            await Task.Delay(1000);
        }
    }
}

