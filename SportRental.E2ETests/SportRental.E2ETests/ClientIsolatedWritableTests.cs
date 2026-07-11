using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace SportRental.E2ETests;

[TestFixture]
[Category("ClientIsolatedWritable")]
[NonParallelizable]
public sealed class ClientIsolatedWritableTests : PageTest
{
    private string _adminUrl = null!;
    private string _clientUrl = null!;
    private string _email = null!;
    private string _password = null!;
    private string _artifactDir = null!;

    [SetUp]
    public async Task SetUpIsolatedWritableTestAsync()
    {
        if (!IsEnabled(Environment.GetEnvironmentVariable("SR_E2E_ALLOW_WRITES")))
            Assert.Ignore("Test zapisu wymaga jawnego SR_E2E_ALLOW_WRITES=1.");

        _adminUrl = RequireEnvironment("SR_ADMIN_URL").TrimEnd('/');
        var target = new Uri(_adminUrl, UriKind.Absolute);
        if (!target.IsLoopback)
            Assert.Fail("ClientIsolatedWritable może działać wyłącznie przeciw hostowi loopback.");

        _clientUrl = $"{_adminUrl}/_client";
        _email = RequireEnvironment("SR_E2E_TEST_EMAIL");
        _password = RequireEnvironment("SR_E2E_TEST_PASSWORD");
        _artifactDir = Environment.GetEnvironmentVariable("SR_E2E_ARTIFACT_DIR")
            ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "client-isolated-writable");
        Directory.CreateDirectory(_artifactDir);

        Page.SetDefaultTimeout(120_000);
        await Page.SetViewportSizeAsync(1440, 1000);
    }

    [Test]
    public async Task Marketplace_MultiTenantCheckoutConflictOwnerPreviewAndLogout_WorkTogether()
    {
        var pageErrors = new List<string>();
        Page.PageError += (_, error) => pageErrors.Add(error);

        await LoginAsync();
        Assert.That(await FetchStatusAsync(Page, "/api/auth/me"), Is.EqualTo(200));

        await OpenCatalogAsync(Page);
        await Expect(Page.Locator("article.product-card")).ToHaveCountAsync(4);
        await Expect(Page.Locator("body")).ToContainTextAsync("Rowerowa Przystań");
        await Expect(Page.Locator("body")).ToContainTextAsync("Górski Zakątek");
        await ScreenshotAsync(Page, "01-catalog-two-tenants");

        await AddProductAsync(Page, "Rower trekkingowy Riverside 500");
        await AddProductAsync(Page, "Narty all-mountain 170 cm");

        await Page.GotoAsync($"{_clientUrl}/cart", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.Locator("section.cart-tenant-group")).ToHaveCountAsync(2);
        await Expect(Page.Locator(".cart-multi-tenant-notice-desktop"))
            .ToContainTextAsync("Każda wypożyczalnia przygotuje osobną rezerwację");
        await Expect(Page.Locator("body")).ToContainTextAsync("ul. Nadwiślańska 12, Warszawa");
        await Expect(Page.Locator("body")).ToContainTextAsync("ul. Krupówki 45, Zakopane");
        await Expect(Page.Locator("body")).ToContainTextAsync("2 punkty odbioru");
        await Expect(Page.Locator(".cart-hold-missing")).ToHaveCountAsync(0);
        await ScreenshotAsync(Page, "02-cart-separated-by-tenant");

        await Page.Locator("button.cart-checkout-btn").ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/_client/checkout(?:\?|$)"));
        await Expect(Page.Locator("article.checkout-rental-group-summary")).ToHaveCountAsync(2);
        await Expect(Page.Locator("body")).ToContainTextAsync("Rowerowa Przystań");
        await Expect(Page.Locator("body")).ToContainTextAsync("Górski Zakątek");

        await Page.Locator("#checkout-fullname").FillAsync("Właściciel E2E");
        await Page.Locator("#checkout-email").FillAsync(_email);
        var phone = Page.GetByRole(AriaRole.Textbox, new() { Name = "Numer telefonu *", Exact = true });
        await Expect(phone).ToHaveCountAsync(1);
        await phone.FillAsync(string.Empty);
        await phone.PressSequentiallyAsync("500100101", new() { Delay = 40 });
        // PhoneInput celowo waliduje i normalizuje numer po debounce (500 ms).
        await Page.WaitForTimeoutAsync(1_000);
        await Expect(phone).ToHaveValueAsync("500100101");
        await Expect(Page.GetByText("Telefon jest wymagany.", new() { Exact = true })).ToHaveCountAsync(0);

        var tenantTerms = Page.Locator(".checkout-tenant-legal-ack input[type='checkbox']");
        await Expect(tenantTerms).ToHaveCountAsync(2);
        for (var index = 0; index < await tenantTerms.CountAsync(); index++)
            await tenantTerms.Nth(index).CheckAsync();
        await Page.Locator(".checkout-legal-ack input[type='checkbox']").CheckAsync();

        var submit = Page.Locator("button.checkout-submit-btn");
        await Expect(submit).ToBeEnabledAsync();
        await ScreenshotAsync(Page, "03-checkout-two-rentals-and-terms");
        await submit.ClickAsync();
        await Expect(Page.Locator(".mud-snackbar").Last)
            .ToContainTextAsync("Nie udało się rozpocząć płatności", new() { Timeout = 30_000 });
        await Expect(Page).ToHaveURLAsync(new Regex(@"/_client/checkout(?:\?|$)"));
        await ScreenshotAsync(Page, "04-checkout-controlled-stripe-boundary");

        await VerifyConflictingHoldAsync();

        // Symulujemy dokładnie przejście z panelu: zostaje cookie Identity, ale usuwamy
        // klientowy JWT. Endpoint preview ma wystawić go ponownie i otworzyć WASM.
        await Page.Context.ClearCookiesAsync(new() { Name = "__Host-sr_access_token" });
        Assert.That(await FetchStatusAsync(Page, "/api/auth/me"), Is.EqualTo(401));
        await Page.GotoAsync($"{_adminUrl}/Account/Login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var preview = Page.GetByRole(AriaRole.Link, new() { Name = "Przejdź do aplikacji klienta" });
        await Expect(preview).ToBeVisibleAsync();
        await ScreenshotAsync(Page, "05-owner-login-page-preview-action");
        await preview.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/_client/?(?:\?|$)"));
        Assert.That(await FetchStatusAsync(Page, "/api/auth/me"), Is.EqualTo(200));
        Assert.That(
            (await Page.Context.CookiesAsync()).Any(cookie => cookie.Name == "__Host-sr_access_token"),
            Is.True,
            "Owner preview nie wystawił klientowego cookie JWT.");
        await ScreenshotAsync(Page, "06-owner-preview-authenticated-client");

        var logout = Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Wyloguj") }).First;
        await Expect(logout).ToBeVisibleAsync();
        var logoutResponse = Page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" && response.Url.EndsWith("/api/auth/logout", StringComparison.Ordinal));
        await logout.ClickAsync();
        Assert.That((await logoutResponse).Status, Is.EqualTo(200));
        await Page.WaitForURLAsync(new Regex(@"/_client/?(?:\?|$)"));
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Page.WaitForTimeoutAsync(500);
        Assert.That(await FetchStatusAsync(Page, "/api/auth/me"), Is.EqualTo(401));
        var remainingCookies = await Page.Context.CookiesAsync();
        Assert.That(remainingCookies.Any(cookie =>
            cookie.Name == "__Host-sr_access_token" ||
            cookie.Name.Contains("Identity.Application", StringComparison.Ordinal)), Is.False);

        await Page.GotoAsync($"{_adminUrl}/Account/Login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.Locator("form.auth-form")).ToBeVisibleAsync();
        await Expect(Page.Locator("body")).Not.ToContainTextAsync("Jesteś zalogowany jako");
        await ScreenshotAsync(Page, "07-logout-cleared-both-sessions");

        Assert.That(pageErrors, Is.Empty, string.Join(Environment.NewLine, pageErrors));
    }

    private async Task LoginAsync()
    {
        await Page.GotoAsync($"{_clientUrl}/login", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Page.Locator("input[type='email']").FillAsync(_email);
        await Page.Locator("input[type='password']").FillAsync(_password);
        await Page.Locator("button.login-button").ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/_client/(?!login)(?:.*)$"));
    }

    private async Task OpenCatalogAsync(IPage page)
    {
        await page.GotoAsync($"{_clientUrl}/products", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("article.product-card").First.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = 120_000
        });
    }

    private async Task AddProductAsync(IPage page, string productName)
    {
        var card = page.Locator("article.product-card").Filter(new() { HasText = productName });
        await Expect(card).ToHaveCountAsync(1);
        var holdResponse = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" && response.Url.EndsWith("/api/holds", StringComparison.Ordinal));
        await card.Locator("button.product-add-btn").ClickAsync();
        Assert.That((await holdResponse).Status, Is.EqualTo(201));
        await Expect(page.Locator(".mud-snackbar").Last).ToContainTextAsync("dodany do koszyka");
    }

    private async Task VerifyConflictingHoldAsync()
    {
        var localDate = DateTime.Today.AddDays(1);
        var polishZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localDate.AddHours(9), DateTimeKind.Unspecified),
            polishZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localDate.AddDays(1).AddHours(17), DateTimeKind.Unspecified),
            polishZone);

        var result = await Page.EvaluateAsync<ConflictHoldResult>(
            """
            async payload => {
                const response = await fetch(payload.url, {
                    method: 'POST',
                    credentials: 'include',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload.body)
                });
                return { status: response.status, body: await response.text() };
            }
            """,
            new
            {
                url = $"{_adminUrl}/api/holds",
                body = new
                {
                    productId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                    quantity = 1,
                    startDateUtc = startUtc.ToString("O"),
                    endDateUtc = endUtc.ToString("O"),
                    ttlMinutes = 10,
                    sessionId = Guid.NewGuid().ToString("N")
                }
            });

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Status, Is.EqualTo(409));
            Assert.That(result.Body, Does.Contain("Brak dostępności"));
        });
    }

    private async Task<int> FetchStatusAsync(IPage page, string path)
    {
        return await page.EvaluateAsync<int>(
            "async url => (await fetch(url, { credentials: 'include', cache: 'no-store' })).status",
            $"{_adminUrl}{path}");
    }

    private async Task ScreenshotAsync(IPage page, string name)
    {
        var path = Path.Combine(_artifactDir, $"{name}.png");
        await page.ScreenshotAsync(new() { Path = path, FullPage = true });
        TestContext.AddTestAttachment(path);
    }

    private static string RequireEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Brak wymaganej zmiennej {name}.");

    private static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private sealed class ConflictHoldResult
    {
        public int Status { get; set; }
        public string Body { get; set; } = string.Empty;
    }
}
