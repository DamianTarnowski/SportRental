using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

public class BaseTest : PageTest
{
    protected const string BaseUrl = "http://localhost:5014";
    protected const string ApiBaseUrl = "http://localhost:5001";

    [SetUp]
    public async Task Setup()
    {
        // Ustaw viewport dla responsywności
        await Page.SetViewportSizeAsync(1920, 1080);
        
        // Ustaw dłuższy timeout dla operacji
        Page.SetDefaultTimeout(15000);
    }

    /// <summary>
    /// Pomocnicza metoda do zrobienia screenshota
    /// </summary>
    protected async Task TakeScreenshotAsync(string name)
    {
        // Użyj bezwzględnej ścieżki
        var screenshotsDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "screenshots");
        Directory.CreateDirectory(screenshotsDir);
        
        var screenshotPath = Path.Combine(screenshotsDir, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = screenshotPath,
            FullPage = true
        });
        
        Console.WriteLine($"Screenshot saved: {screenshotPath}");
    }

    /// <summary>
    /// Czeka aż strona się załaduje (brak spinnera)
    /// </summary>
    protected async Task WaitForPageLoadAsync()
    {
        // Czekaj aż zniknie główny spinner/loader
        try
        {
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10000 });
        }
        catch
        {
            // Ignoruj timeout - czasem NetworkIdle nie zadziała dla WASM
            await Task.Delay(1000);
        }
    }
}

