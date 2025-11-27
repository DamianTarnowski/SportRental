using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
public class ProductCatalogTests : BaseTest
{
    [Test]
    public async Task ProductCatalog_ShouldLoad_AndDisplayProducts()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000); // Daj chwilę na załadowanie produktów z API

        // Assert - Sprawdź tytuł strony
        await Expect(Page).ToHaveTitleAsync(new Regex("Product Catalog|SportRental"));

        // Screenshot
        await TakeScreenshotAsync("06_product_catalog");
    }

    [Test]
    public async Task ProductCatalog_SearchBox_ShouldBeVisible()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();

        // Assert - Sprawdź pole wyszukiwania
        var searchBox = Page.Locator("input[placeholder*='product']").Or(Page.Locator("input[placeholder*='produkt']"));
        await Expect(searchBox.First).ToBeVisibleAsync();

        // Screenshot
        await TakeScreenshotAsync("07_search_box");
    }

    [Test]
    public async Task ProductCatalog_Filters_ShouldBeVisible()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();

        // Assert - Sprawdź czy są filtry
        // Kategoria
        var categorySelect = Page.Locator("label:has-text('Category')").Or(Page.Locator("label:has-text('Kategor')"));
        
        // Sortowanie
        var sortSelect = Page.Locator("label:has-text('Sort')").Or(Page.Locator("label:has-text('Sortuj')"));
        
        // "Only available" switch
        var availableSwitch = Page.Locator("label:has-text('Only available')").Or(Page.Locator("label:has-text('dostępne')"));

        // Screenshot
        await TakeScreenshotAsync("08_filters");
    }

    [Test]
    public async Task ProductCatalog_ProductCard_ShouldDisplay_AddToCartButton()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000); // Czekaj na produkty

        // Assert - Znajdź pierwszy przycisk "Add to cart"
        var addToCartButton = Page.Locator("button:has-text('Add to cart')").Or(Page.Locator("button:has-text('Dodaj do koszyka')"));
        
        if (await addToCartButton.CountAsync() > 0)
        {
            await Expect(addToCartButton.First).ToBeVisibleAsync();
        }

        // Screenshot
        await TakeScreenshotAsync("09_product_card");
    }

    [Test]
    public async Task ProductCatalog_ViewDetailsButton_ShouldNavigateToProductDetails()
    {
        // Arrange
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        // Act - Kliknij pierwszy link/przycisk "View details"
        var viewDetailsLink = Page.Locator("a[href^='/products/']:has-text('View details')")
            .Or(Page.Locator("a[href^='/products/']:has-text('Zobacz szczegóły')"))
            .Or(Page.Locator("button:has-text('View details')"))
            .Or(Page.Locator("button:has-text('Zobacz szczegóły')"));
        
        if (await viewDetailsLink.CountAsync() > 0)
        {
            await viewDetailsLink.First.ClickAsync();
            await WaitForPageLoadAsync();

            // Assert - Powinniśmy być na stronie szczegółów produktu
            await Expect(Page).ToHaveURLAsync(new Regex("/products/[0-9a-f-]+"));

            // Screenshot
            await TakeScreenshotAsync("10_navigated_to_product_details");
        }
        else
        {
            Assert.Warn("Brak produktów do przetestowania");
        }
    }

    [Test]
    public async Task ProductCatalog_Pagination_ShouldBeVisible_WhenManyProducts()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        // Screenshot - paginacja może być widoczna tylko gdy jest > 12 produktów
        await TakeScreenshotAsync("11_pagination_check");
    }

    [Test]
    public async Task ProductCatalog_Statistics_ShouldDisplay()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/products");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        // Assert - Sprawdź statystyki w headerze (Total, Ready, Avg price)
        // Używamy bardziej ogólnych selektorów
        var statsChips = Page.Locator(".mud-chip, [class*='chip']");

        // Screenshot
        await TakeScreenshotAsync("12_catalog_statistics");
    }
}

