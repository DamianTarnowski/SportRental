using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
public class HomePageTests : BaseTest
{
    [Test]
    public async Task HomePage_ShouldLoad_AndDisplayHeroSection()
    {
        // Arrange & Act
        await Page.GotoAsync(BaseUrl);
        await WaitForPageLoadAsync();

        // Assert - Sprawdź tytuł
        await Expect(Page).ToHaveTitleAsync(new Regex("SportRental"));

        // Assert - Sprawdź Hero section
        var heroHeading = Page.Locator("h1:has-text('Twoja przygoda')");
        await Expect(heroHeading).ToBeVisibleAsync();

        // Screenshot
        await TakeScreenshotAsync("01_home_page");
    }

    [Test]
    public async Task HomePage_BrowseEquipmentButton_ShouldNavigateToProducts()
    {
        // Arrange
        await Page.GotoAsync(BaseUrl);
        await WaitForPageLoadAsync();

        // Act - Kliknij "Przeglądaj sprzęt"
        var browseButton = Page.Locator("a:has-text('Przeglądaj sprzęt')").First;
        await browseButton.ClickAsync();
        await WaitForPageLoadAsync();

        // Assert - Powinniśmy być na /products
        await Expect(Page).ToHaveURLAsync(new Regex("/products"));

        // Screenshot
        await TakeScreenshotAsync("02_navigated_to_products");
    }

    [Test]
    public async Task HomePage_ShouldDisplay_FeaturesSection()
    {
        // Arrange & Act
        await Page.GotoAsync(BaseUrl);
        await WaitForPageLoadAsync();

        // Assert - Sprawdź sekcję "Dlaczego warto wybrać nas?"
        var featuresHeading = Page.Locator("h2:has-text('Dlaczego warto')");
        await Expect(featuresHeading).ToBeVisibleAsync();

        // Sprawdź czy są 3 feature cards
        var featureCards = Page.Locator(".rounded-xl.border.bg-white.p-6");
        await Expect(featureCards).ToHaveCountAsync(3);

        // Screenshot
        await TakeScreenshotAsync("03_home_features");
    }

    [Test]
    public async Task Navigation_CartIcon_ShouldBeVisible()
    {
        // Arrange & Act
        await Page.GotoAsync(BaseUrl);
        await WaitForPageLoadAsync();

        // Assert - Sprawdź ikonę koszyka w headerze (first bo jest także w menu)
        var cartIcon = Page.Locator("a[href='/cart'] svg").First;
        await Expect(cartIcon).ToBeVisibleAsync();

        // Screenshot
        await TakeScreenshotAsync("04_cart_icon");
    }

    [Test]
    public async Task Navigation_Menu_ShouldContainAllLinks()
    {
        // Arrange & Act
        await Page.GotoAsync(BaseUrl);
        await WaitForPageLoadAsync();

        // Assert - Sprawdź linki w menu
        var homeLink = Page.Locator("a:has-text('Strona główna')").First;
        var productsLink = Page.Locator("a:has-text('Katalog produktów')").First;
        var cartLink = Page.Locator("a:has-text('Koszyk')").First;
        var contactLink = Page.Locator("a:has-text('Kontakt')").First;

        await Expect(homeLink).ToBeVisibleAsync();
        await Expect(productsLink).ToBeVisibleAsync();
        await Expect(cartLink).ToBeVisibleAsync();
        await Expect(contactLink).ToBeVisibleAsync();

        // Screenshot
        await TakeScreenshotAsync("05_navigation_menu");
    }
}

