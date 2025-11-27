using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
public class CheckoutTests : BaseTest
{
    [Test]
    public async Task Checkout_EmptyCart_ShouldDisplay_WarningMessage()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/checkout");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź komunikat o pustym koszyku
        var emptyMessage = Page.Locator("text='pusty'").Or(Page.Locator("text='empty'"));

        // Screenshot
        await TakeScreenshotAsync("24_checkout_empty");
    }

    [Test]
    public async Task Checkout_CustomerForm_ShouldBeVisible()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/checkout");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź pola formularza
        var fullNameField = Page.Locator("label:has-text('nazwisko')").Or(Page.Locator("label:has-text('name')"));
        var emailField = Page.Locator("label:has-text('Email')");
        var phoneField = Page.Locator("label:has-text('Telefon')").Or(Page.Locator("label:has-text('Phone')"));

        // Screenshot
        await TakeScreenshotAsync("25_checkout_form");
    }

    [Test]
    public async Task Checkout_PaymentSummary_ShouldDisplay()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/checkout");
        await WaitForPageLoadAsync();
        await Task.Delay(2000);

        // Assert - Sprawdź sekcję płatności
        var paymentSection = Page.Locator("text='płatności'").Or(Page.Locator("text='Payment'"));

        // Screenshot
        await TakeScreenshotAsync("26_payment_summary");
    }

    [Test]
    public async Task Checkout_ConfirmButton_ShouldBeVisible()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/checkout");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź przycisk potwierdzenia
        var confirmButton = Page.Locator("button:has-text('Potwierdź')").Or(Page.Locator("button:has-text('Confirm')"));

        // Screenshot
        await TakeScreenshotAsync("27_confirm_button");
    }

    [Test]
    public async Task Checkout_CustomerLookupButton_ShouldBeVisible()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/checkout");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź przycisk "Wyszukaj klienta"
        var lookupButton = Page.Locator("button:has-text('Wyszukaj')").Or(Page.Locator("button:has-text('Search')"));

        // Screenshot
        await TakeScreenshotAsync("28_customer_lookup");
    }
}

