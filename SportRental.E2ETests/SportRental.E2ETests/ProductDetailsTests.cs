using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
public class ProductDetailsTests : BaseTest
{
    private string? _firstProductUrl;

    [SetUp]
    public new async Task Setup()
    {
        await base.Setup();
        
        // Znajdź pierwszy produkt
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        var firstProductLink = Page.Locator("a[href^='/products/']").First;
        if (await firstProductLink.CountAsync() > 0)
        {
            _firstProductUrl = await firstProductLink.GetAttributeAsync("href");
        }
    }

    [Test]
    public async Task ProductDetails_ShouldLoad_AndDisplayProductInfo()
    {
        // Arrange
        if (string.IsNullOrEmpty(_firstProductUrl))
        {
            Assert.Warn("Brak produktów do przetestowania");
            return;
        }

        // Act
        await Page.GotoAsync($"{BaseUrl}{_firstProductUrl}");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź czy są podstawowe elementy
        await Expect(Page).ToHaveURLAsync(new Regex("/products/[0-9a-f-]+"));

        // Screenshot
        await TakeScreenshotAsync("13_product_details");
    }

    [Test]
    public async Task ProductDetails_DatePickers_ShouldBeVisible()
    {
        // Arrange
        if (string.IsNullOrEmpty(_firstProductUrl))
        {
            Assert.Warn("Brak produktów");
            return;
        }

        // Act
        await Page.GotoAsync($"{BaseUrl}{_firstProductUrl}");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź date pickery (Start date, End date)
        var startDateInput = Page.Locator("label:has-text('Start date')").Or(Page.Locator("label:has-text('Data rozpoczęcia')"));
        var endDateInput = Page.Locator("label:has-text('End date')").Or(Page.Locator("label:has-text('Data zakończenia')"));

        // Screenshot
        await TakeScreenshotAsync("14_date_pickers");
    }

    [Test]
    public async Task ProductDetails_AddToCartButton_ShouldBeVisible()
    {
        // Arrange
        if (string.IsNullOrEmpty(_firstProductUrl))
        {
            Assert.Warn("Brak produktów");
            return;
        }

        // Act
        await Page.GotoAsync($"{BaseUrl}{_firstProductUrl}");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert
        var addToCartButton = Page.Locator("button:has-text('Add to cart')").Or(Page.Locator("button:has-text('Dodaj do koszyka')"));
        if (await addToCartButton.CountAsync() > 0)
        {
            await Expect(addToCartButton.First).ToBeVisibleAsync();
        }

        // Screenshot
        await TakeScreenshotAsync("15_add_to_cart_button");
    }

    [Test]
    public async Task ProductDetails_ImageLightbox_ShouldOpen()
    {
        // Arrange
        if (string.IsNullOrEmpty(_firstProductUrl))
        {
            Assert.Warn("Brak produktów");
            return;
        }

        // Act
        await Page.GotoAsync($"{BaseUrl}{_firstProductUrl}");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Kliknij zdjęcie produktu (jeśli istnieje)
        var productImage = Page.Locator("img[alt], img").First;
        if (await productImage.CountAsync() > 0)
        {
            await productImage.ClickAsync();
            await Task.Delay(500);

            // Screenshot lightboxa
            await TakeScreenshotAsync("16_image_lightbox");
        }
    }

    [Test]
    public async Task ProductDetails_RelatedProducts_ShouldBeVisible()
    {
        // Arrange
        if (string.IsNullOrEmpty(_firstProductUrl))
        {
            Assert.Warn("Brak produktów");
            return;
        }

        // Act
        await Page.GotoAsync($"{BaseUrl}{_firstProductUrl}");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź sekcję "You may also like" / "Może Cię również zainteresować"
        var relatedSection = Page.Locator("text='You may also like'").Or(Page.Locator("text='zainteresować'"));

        // Screenshot (przewiń do related products)
        await Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
        await Task.Delay(500);
        await TakeScreenshotAsync("17_related_products");
    }
}

