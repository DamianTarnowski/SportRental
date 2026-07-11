using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework.Interfaces;

namespace SportRental.E2ETests;

/// <summary>
/// Infrastructure for production-safe Client WASM checks. The fixture deliberately exposes
/// navigation and inspection helpers only; tests never create accounts, holds or rentals.
/// </summary>
public abstract class ClientReadOnlyTestBase : PageTest
{
    private static readonly string RunId = SanitizeFileName(
        Environment.GetEnvironmentVariable("SR_E2E_RUN_ID")
        ?? DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));

    private static readonly Regex SensitiveValuePattern = new(
        @"(?<key>(?:access[_-]?token|refresh[_-]?token|token|code|signature|sig|secret|password|api[_-]?key|key)=)[^&\s\""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly object _sync = new();
    private readonly List<string> _report = [];
    private readonly List<string> _pageErrors = [];
    private readonly List<string> _criticalConsoleErrors = [];
    private readonly List<string> _criticalNetworkErrors = [];
    private readonly List<int> _bootManifestStatuses = [];
    private readonly Dictionary<IRequest, (int Generation, string DocumentUrl)> _requestNavigationContexts =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> _controlledDocumentLeaveGenerations =
        new(StringComparer.OrdinalIgnoreCase);

    private string _artifactDirectory = string.Empty;
    private int _navigationGeneration;

    protected Uri AdminRoot { get; private set; } = null!;

    [SetUp]
    public async Task SetUpClientReadOnlyTestAsync()
    {
        // NUnit reuses the fixture instance between tests. Keep every report and assertion
        // scoped to the current Page instead of carrying diagnostics from an earlier test.
        lock (_sync)
        {
            _report.Clear();
            _pageErrors.Clear();
            _criticalConsoleErrors.Clear();
            _criticalNetworkErrors.Clear();
            _bootManifestStatuses.Clear();
            _requestNavigationContexts.Clear();
            _controlledDocumentLeaveGenerations.Clear();
            _navigationGeneration = 0;
        }

        _artifactDirectory = CreateArtifactDirectory();
        AdminRoot = ReadAdminRoot();

        Page.SetDefaultTimeout(30_000);
        Page.SetDefaultNavigationTimeout(60_000);
        await Page.SetViewportSizeAsync(1440, 1000);

        AttachDiagnostics();
        Report($"START {DateTime.UtcNow:O}");
        Report($"Test: {TestContext.CurrentContext.Test.FullName}");
        Report($"Target: {AdminRoot.Scheme}://{AdminRoot.Authority}");
    }

    [TearDown]
    public async Task TearDownClientReadOnlyTestAsync()
    {
        Report($"Page errors: {_pageErrors.Count}");
        Report($"Critical console errors: {_criticalConsoleErrors.Count}");
        Report($"Critical network errors: {_criticalNetworkErrors.Count}");
        Report($"END {DateTime.UtcNow:O}");

        if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Passed)
        {
            try
            {
                await CaptureScreenshotAsync("failure");
            }
            catch (Exception exception)
            {
                Report($"Could not capture failure screenshot: {Redact(exception.Message)}");
            }
        }

        var reportPath = Path.Combine(_artifactDirectory, "report.txt");
        string contents;
        lock (_sync)
        {
            contents = string.Join(Environment.NewLine, _report) + Environment.NewLine;
        }

        await File.WriteAllTextAsync(reportPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        TestContext.AddTestAttachment(reportPath, "Client WASM read-only diagnostics");
    }

    protected string ClientUrl(string route = "/")
    {
        var normalized = string.IsNullOrWhiteSpace(route) || route == "/"
            ? string.Empty
            : route.TrimStart('/');

        return new Uri(AdminRoot, $"/_client/{normalized}").AbsoluteUri;
    }

    protected async Task<IResponse?> OpenClientRouteAsync(string route = "/")
    {
        var target = ClientUrl(route);
        Report($"Navigate: {SanitizeUrl(target)}");

        // Tag every request with the document generation in which it started. Requests
        // from the outgoing document may be cancelled asynchronously after GotoAsync
        // has already returned, so a short-lived boolean cannot classify them reliably.
        lock (_sync)
        {
            var outgoingDocumentUrl = NormalizeDocumentUrl(Page.Url);
            _navigationGeneration++;
            _controlledDocumentLeaveGenerations[outgoingDocumentUrl] = _navigationGeneration;
        }

        var response = await Page.GotoAsync(target, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });

        Assert.That(response, Is.Not.Null, $"Brak odpowiedzi dokumentu dla {SanitizeUrl(target)}.");
        Assert.That(response!.Status, Is.InRange(200, 399),
            $"Dokument {SanitizeUrl(target)} zwrócił HTTP {response.Status}.");

        await Expect(Page.GetByText("Sprawdzanie uprawnień...", new PageGetByTextOptions { Exact = true }))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions
            {
                Timeout = 60_000
            });
        await Expect(Page.Locator("main").First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 60_000
        });
        await WaitForLayoutToSettleAsync();
        AssertClientLocation(route);

        Report($"Loaded: {SanitizeUrl(Page.Url)} (HTTP {response.Status})");
        return response;
    }

    protected void AssertClientLocation(string context)
    {
        var current = new Uri(Page.Url);
        var staysInClient = current.AbsolutePath.Equals("/_client", StringComparison.OrdinalIgnoreCase)
                            || current.AbsolutePath.StartsWith("/_client/", StringComparison.OrdinalIgnoreCase);

        Assert.That(staysInClient, Is.True,
            $"Trasa '{context}' opuściła aplikację Client WASM: {SanitizeUrl(current.AbsoluteUri)}.");
    }

    protected async Task AssertWasmBootedAsync()
    {
        var runtime = await Page.EvaluateAsync<WasmRuntimeState>(
            """
            () => {
                const app = document.querySelector('#app');
                const errorUi = document.querySelector('#blazor-error-ui');
                const errorUiVisible = errorUi !== null
                    && getComputedStyle(errorUi).display !== 'none'
                    && errorUi.getBoundingClientRect().width > 0
                    && errorUi.getBoundingClientRect().height > 0;

                return {
                    hasAppRoot: app !== null,
                    hasRenderedClientUi: app?.querySelector('main') !== null,
                    hasBlazorRuntime: typeof globalThis.Blazor === 'object'
                        && globalThis.Blazor !== null,
                    hasVisibleBootError: errorUiVisible
                };
            }
            """);

        int[] statuses;
        lock (_sync)
        {
            statuses = [.. _bootManifestStatuses];
        }

        Assert.Multiple(() =>
        {
            Assert.That(runtime.HasAppRoot, Is.True,
                "Nie znaleziono korzenia #app aplikacji Client WASM.");
            Assert.That(runtime.HasRenderedClientUi, Is.True,
                "Client WASM nie wyrenderował głównego interfejsu wewnątrz #app.");
            Assert.That(runtime.HasBlazorRuntime, Is.True,
                "Globalny runtime Blazor nie jest dostępny po wyrenderowaniu klienta.");
            Assert.That(runtime.HasVisibleBootError, Is.False,
                "Blazor pokazał interfejs krytycznego błędu uruchomienia.");

            // A cached response can be delivered before Playwright attaches its response
            // observer. Treat observed statuses as extra diagnostics, not the boot signal.
            if (statuses.Length > 0)
            {
                Assert.That(statuses, Has.All.InRange(200, 299),
                    $"Manifest startowy Blazor zwrócił niepoprawny status: {string.Join(", ", statuses)}.");
            }
        });

        var manifestObservation = statuses.Length > 0
            ? string.Join(", ", statuses)
            : "nieprzechwycony (cache lub odpowiedź sprzed obserwatora)";
        Report(
            $"WASM runtime: app={runtime.HasAppRoot}, ui={runtime.HasRenderedClientUi}, "
            + $"blazor={runtime.HasBlazorRuntime}, bootError={runtime.HasVisibleBootError}; "
            + $"manifest={manifestObservation}");
    }

    protected async Task AssertSameOriginLinksStayInClientAsync(string context)
    {
        var hrefs = await Page.Locator("a[href]").EvaluateAllAsync<string[]>(
            "elements => elements.map(element => element.getAttribute('href')).filter(Boolean)");

        var escapedLinks = new List<string>();
        var allowedAuthHandoffs = new List<string>();
        foreach (var href in hrefs.Distinct(StringComparer.Ordinal))
        {
            if (!Uri.TryCreate(new Uri(Page.Url), href, out var resolved)
                || !IsSameOrigin(resolved, AdminRoot))
            {
                continue;
            }

            if (!resolved.AbsolutePath.Equals("/_client", StringComparison.OrdinalIgnoreCase)
                && !resolved.AbsolutePath.StartsWith("/_client/", StringComparison.OrdinalIgnoreCase))
            {
                if (IsAllowedClientAuthHandoff(resolved.AbsolutePath))
                {
                    allowedAuthHandoffs.Add($"{href} -> {SanitizeUrl(resolved.AbsoluteUri)}");
                    continue;
                }

                escapedLinks.Add($"{href} -> {SanitizeUrl(resolved.AbsoluteUri)}");
            }
        }

        Report(
            $"Same-origin links checked ({context}): {hrefs.Length}; "
            + $"allowed auth handoffs: {allowedAuthHandoffs.Count}; escaped: {escapedLinks.Count}");
        Assert.That(escapedLinks, Is.Empty,
            $"Linki na stronie '{context}' wychodzą poza /_client/:\n{string.Join(Environment.NewLine, escapedLinks)}");
    }

    protected async Task AssertNoHorizontalOverflowAsync(string context)
    {
        var metrics = await Page.EvaluateAsync<OverflowMetrics>(
            """
            () => {
                const root = document.documentElement;
                const body = document.body;
                const viewportWidth = root.clientWidth;
                const documentWidth = root.scrollWidth;
                const bodyWidth = body ? body.scrollWidth : 0;
                const maxWidth = Math.max(documentWidth, bodyWidth);
                const offenders = maxWidth <= viewportWidth + 1
                    ? []
                    : [...document.querySelectorAll('body *')]
                        .map(element => {
                            const rect = element.getBoundingClientRect();
                            const style = getComputedStyle(element);
                            return { element, rect, style };
                        })
                        .filter(item => item.style.display !== 'none'
                            && item.style.visibility !== 'hidden'
                            && item.rect.width > 0
                            && (item.rect.right > viewportWidth + 1 || item.rect.left < -1))
                        .slice(0, 12)
                        .map(item => {
                            const element = item.element;
                            const id = element.id ? `#${element.id}` : '';
                            const classes = [...element.classList].slice(0, 3).map(value => `.${value}`).join('');
                            return `${element.tagName.toLowerCase()}${id}${classes}`;
                        });

                return { viewportWidth, documentWidth, bodyWidth, offenders };
            }
            """);

        Report(
            $"Overflow ({context}): viewport={metrics.ViewportWidth}, document={metrics.DocumentWidth}, "
            + $"body={metrics.BodyWidth}, offenders=[{string.Join(", ", metrics.Offenders)}]");

        Assert.That(Math.Max(metrics.DocumentWidth, metrics.BodyWidth),
            Is.LessThanOrEqualTo(metrics.ViewportWidth + 1),
            $"Poziomy overflow na '{context}'. Elementy: {string.Join(", ", metrics.Offenders)}");
    }

    protected async Task WaitForLayoutToSettleAsync()
    {
        try
        {
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
            {
                Timeout = 5_000
            });
        }
        catch (TimeoutException)
        {
            Report("NetworkIdle timeout (non-fatal for WASM).");
        }

        await Page.EvaluateAsync(
            """
            async () => {
                if (document.fonts?.ready) {
                    await document.fonts.ready;
                }

                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            }
            """);
    }

    protected async Task CaptureScreenshotAsync(string name)
    {
        var path = Path.Combine(_artifactDirectory, $"{SanitizeFileName(name)}.png");
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = true,
            Animations = ScreenshotAnimations.Disabled
        });

        TestContext.AddTestAttachment(path, name);
        Report($"Screenshot: {Path.GetFileName(path)}");
    }

    protected void AssertNoCriticalDiagnostics()
    {
        List<string> issues;
        lock (_sync)
        {
            issues = [
                .. _pageErrors.Select(error => $"PAGE: {error}"),
                .. _criticalConsoleErrors.Select(error => $"CONSOLE: {error}"),
                .. _criticalNetworkErrors.Select(error => $"NETWORK: {error}")
            ];
        }

        Assert.That(issues, Is.Empty,
            $"Wykryto krytyczne błędy Client WASM:{Environment.NewLine}{string.Join(Environment.NewLine, issues)}");
    }

    protected void ReportObservation(string value) => Report(value);

    private void AttachDiagnostics()
    {
        Page.Request += (_, request) =>
        {
            lock (_sync)
            {
                _requestNavigationContexts[request] =
                    (_navigationGeneration, NormalizeDocumentUrl(request.Frame.Url));
            }
        };

        Page.RequestFinished += (_, request) =>
        {
            lock (_sync)
            {
                _requestNavigationContexts.Remove(request);
            }
        };

        Page.Console += (_, message) =>
        {
            if (!message.Type.Equals("warning", StringComparison.OrdinalIgnoreCase)
                && !message.Type.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var entry = $"[{message.Type}] {Redact(message.Text)}";
            Report($"Console {entry}");
            if (IsCriticalConsoleMessage(message))
            {
                lock (_sync)
                {
                    _criticalConsoleErrors.Add(entry);
                }
            }
        };

        Page.PageError += (_, error) =>
        {
            var entry = Redact(error);
            lock (_sync)
            {
                _pageErrors.Add(entry);
            }
            Report($"Page error: {entry}");
        };

        Page.RequestFailed += (_, request) =>
        {
            var entry = $"{request.Method} {SanitizeUrl(request.Url)} ({request.ResourceType}): {Redact(request.Failure ?? "unknown failure")}";
            Report($"Request failed: {entry}");

            if (IsFirstParty(request.Url)
                && IsCoreResource(request)
                && !IsExpectedNavigationAbort(request))
            {
                lock (_sync)
                {
                    _criticalNetworkErrors.Add(entry);
                }
            }

            lock (_sync)
            {
                _requestNavigationContexts.Remove(request);
            }
        };

        Page.Response += (_, response) =>
        {
            if (!Uri.TryCreate(response.Url, UriKind.Absolute, out var responseUri))
            {
                return;
            }

            if (responseUri.AbsolutePath.EndsWith("/_framework/blazor.boot.json", StringComparison.OrdinalIgnoreCase))
            {
                lock (_sync)
                {
                    _bootManifestStatuses.Add(response.Status);
                }
            }

            if (response.Status < 400)
            {
                return;
            }

            var entry = $"HTTP {response.Status} {response.Request.Method} {SanitizeUrl(response.Url)} ({response.Request.ResourceType})";
            Report($"HTTP error: {entry}");

            var isUnexpectedCoreError = IsFirstParty(response.Url)
                                        && IsCoreResource(response.Request)
                                        && !IsExpectedAnonymousAuthResponse(responseUri.AbsolutePath, response.Status);
            var isBrokenImage = response.Status == 404
                                && response.Request.ResourceType.Equals("image", StringComparison.OrdinalIgnoreCase);

            if (isUnexpectedCoreError || isBrokenImage)
            {
                lock (_sync)
                {
                    _criticalNetworkErrors.Add(entry);
                }
            }
        };
    }

    private static Uri ReadAdminRoot()
    {
        var configured = Environment.GetEnvironmentVariable("SR_ADMIN_URL");
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "Ustaw SR_ADMIN_URL na adres hosta Admin, np. https://app.example.com. "
                + "Pakiet ClientReadOnly nie wybiera środowiska domyślnie.");
        }

        if (!Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("SR_ADMIN_URL musi być bezpiecznym adresem bez danych logowania (http/https).");
        }

        return new UriBuilder(uri)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }

    private string CreateArtifactDirectory()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("SR_E2E_ARTIFACT_DIR");
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "client-readonly")
            : Path.GetFullPath(configuredRoot);

        var testName = SanitizeFileName(TestContext.CurrentContext.Test.Name);
        var directory = Path.Combine(root, RunId, testName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private bool IsFirstParty(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsSameOrigin(uri, AdminRoot);

    private static bool IsSameOrigin(Uri left, Uri right)
        => left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase)
           && left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase)
           && left.Port == right.Port;

    private static bool IsCoreResource(IRequest request)
    {
        var resourceType = request.ResourceType.ToLowerInvariant();
        if (resourceType is "document" or "script" or "stylesheet" or "fetch" or "xhr" or "wasm")
        {
            return true;
        }

        return Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
               && uri.AbsolutePath.Contains("/_framework/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedAnonymousAuthResponse(string path, int status)
        => status is 401 or 403
           && IsExpectedAnonymousAuthPath(path);

    private bool IsExpectedNavigationAbort(IRequest request)
    {
        if (request.Failure?.Contains("ERR_ABORTED", StringComparison.OrdinalIgnoreCase) != true
            && request.Failure?.Contains("NS_BINDING_ABORTED", StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (IsExpectedAnonymousAuthPath(uri.AbsolutePath))
        {
            return true;
        }

        lock (_sync)
        {
            if (!_requestNavigationContexts.TryGetValue(request, out var context))
            {
                return false;
            }

            if (context.Generation < _navigationGeneration)
            {
                return true;
            }

            // A fetch can start in the tiny interval after a controlled navigation is
            // announced but before Chromium replaces the old document. Its generation is
            // already current, while request.Frame.Url still identifies the document that
            // was deliberately left. The leave generation prevents a later revisit to the
            // same URL from hiding an unrelated abort.
            return _controlledDocumentLeaveGenerations.TryGetValue(
                       context.DocumentUrl,
                       out var leaveGeneration)
                   && context.Generation <= leaveGeneration;
        }
    }

    private static string NormalizeDocumentUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value;
        }

        return uri.GetLeftPart(UriPartial.Path) + uri.Query;
    }

    private static bool IsExpectedAnonymousAuthPath(string path)
        => path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase)
           || path.Equals("/api/auth/refresh", StringComparison.OrdinalIgnoreCase)
           || path.Equals("/Account/Login", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedClientAuthHandoff(string path)
        => path.Equals("/Account/ForgotPassword", StringComparison.OrdinalIgnoreCase)
           || path.Equals("/Account/StartExternalLogin", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedAnonymousAuthConsoleMessage(IConsoleMessage message)
    {
        if (!message.Type.Equals("error", StringComparison.OrdinalIgnoreCase)
            || (!message.Text.Contains("401", StringComparison.OrdinalIgnoreCase)
                && !message.Text.Contains("403", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var locationWithoutCoordinates = Regex.Replace(message.Location ?? string.Empty, @":\d+:\d+$", string.Empty);
        if (Uri.TryCreate(locationWithoutCoordinates, UriKind.Absolute, out var locationUri)
            && IsExpectedAnonymousAuthPath(locationUri.AbsolutePath))
        {
            return true;
        }

        return message.Text.Contains("/api/auth/me", StringComparison.OrdinalIgnoreCase)
               || message.Text.Contains("/api/auth/refresh", StringComparison.OrdinalIgnoreCase)
               || message.Text.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCriticalConsoleMessage(IConsoleMessage message)
    {
        var type = message.Type;
        var text = message.Text;

        if (IsExpectedAnonymousAuthConsoleMessage(message))
        {
            return false;
        }

        // Chromium does not always put the request URL in this generic console entry.
        // The Response handler still validates the concrete URL and treats every non-auth
        // 4xx/5xx core response as critical, so ignoring this duplicate 401 cannot hide it.
        if (type.Equals("error", StringComparison.OrdinalIgnoreCase)
            && text.StartsWith("Failed to load resource:", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("status of 401", StringComparison.OrdinalIgnoreCase)
                || text.Contains("status of 403", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (text.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase)
            || text.Contains("WebAssemblyRenderer[100]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("crit:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Keep resource errors critical. Known anonymous auth responses are filtered above;
        // a real 404 (including an image) must remain visible to the suite.
        return type.Equals("error", StringComparison.OrdinalIgnoreCase);
    }

    private void Report(string value)
    {
        lock (_sync)
        {
            _report.Add(Redact(value));
        }
    }

    private static string SanitizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return Redact(value);
        }

        return uri.GetLeftPart(UriPartial.Path);
    }

    private static string Redact(string value)
        => SensitiveValuePattern.Replace(value, match => $"{match.Groups["key"].Value}<redacted>");

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) || char.IsWhiteSpace(character) ? '_' : character);
        }

        return builder.ToString().Trim('_');
    }
}

public sealed class OverflowMetrics
{
    [JsonPropertyName("viewportWidth")]
    public int ViewportWidth { get; init; }

    [JsonPropertyName("documentWidth")]
    public int DocumentWidth { get; init; }

    [JsonPropertyName("bodyWidth")]
    public int BodyWidth { get; init; }

    [JsonPropertyName("offenders")]
    public string[] Offenders { get; init; } = [];
}

public sealed class WasmRuntimeState
{
    [JsonPropertyName("hasAppRoot")]
    public bool HasAppRoot { get; init; }

    [JsonPropertyName("hasRenderedClientUi")]
    public bool HasRenderedClientUi { get; init; }

    [JsonPropertyName("hasBlazorRuntime")]
    public bool HasBlazorRuntime { get; init; }

    [JsonPropertyName("hasVisibleBootError")]
    public bool HasVisibleBootError { get; init; }
}
