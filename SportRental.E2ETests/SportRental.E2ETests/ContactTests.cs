using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
public class ContactTests : BaseTest
{
    [Test]
    public async Task Contact_Page_ShouldLoad()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/contact");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert
        await Expect(Page).ToHaveURLAsync(new Regex("/contact"));

        // Screenshot
        await TakeScreenshotAsync("29_contact_page");
    }

    [Test]
    public async Task Contact_ContactInformation_ShouldBeVisible()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/contact");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź czy są dane kontaktowe
        var phoneSection = Page.Locator("text='Telefon'").Or(Page.Locator("text='Phone'"));
        var emailSection = Page.Locator("text='Email'");
        var addressSection = Page.Locator("text='Adres'").Or(Page.Locator("text='Address'"));

        // Screenshot
        await TakeScreenshotAsync("30_contact_information");
    }

    [Test]
    public async Task Contact_ContactForm_ShouldBeVisible()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/contact");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Assert - Sprawdź formularz kontaktowy
        var nameField = Page.Locator("label:has-text('Imię')").Or(Page.Locator("label:has-text('Name')"));
        var emailField = Page.Locator("label:has-text('Email')");
        var subjectField = Page.Locator("label:has-text('Temat')").Or(Page.Locator("label:has-text('Subject')"));
        var messageField = Page.Locator("label:has-text('Wiadomość')").Or(Page.Locator("label:has-text('Message')"));

        // Screenshot
        await TakeScreenshotAsync("31_contact_form");
    }

    [Test]
    public async Task Contact_SubmitButton_ShouldBeVisible()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/contact");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Przewiń do formularza
        await Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 2)");
        await Task.Delay(500);

        // Assert
        var submitButton = Page.Locator("button[type='submit']:has-text('Wyślij')").Or(Page.Locator("button:has-text('Send')"));

        // Screenshot
        await TakeScreenshotAsync("32_submit_button");
    }

    [Test]
    public async Task Contact_SocialMedia_ShouldBeVisible()
    {
        // Arrange & Act
        await Page.GotoAsync($"{BaseUrl}/contact");
        await WaitForPageLoadAsync();
        await Task.Delay(1000);

        // Przewiń w dół
        await Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 3)");
        await Task.Delay(500);

        // Screenshot sekcji social media
        await TakeScreenshotAsync("33_social_media");
    }
}

