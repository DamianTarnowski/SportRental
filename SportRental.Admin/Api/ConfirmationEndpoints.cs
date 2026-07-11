using System.Text;
using Microsoft.AspNetCore.Authorization;
using SportRental.Admin.Services;
using SportRental.Admin.Services.Time;

namespace SportRental.Admin.Api;

public static class ConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapConfirmationEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /confirm/{token} — publiczna strona potwierdzenia (HTML)
        app.MapGet("/confirm/{token}", [AllowAnonymous] async (
            string token,
            IRentalConfirmationService confirmationService) =>
        {
            var data = await confirmationService.GetConfirmationDataAsync(token);
            if (data == null)
                return Results.Content(RenderErrorPage("Nieprawidłowy link", "Link potwierdzenia jest nieprawidłowy lub wynajem nie został znaleziony."), "text/html; charset=utf-8");

            if (data.IsExpired && !data.IsAlreadyConfirmed)
                return Results.Content(RenderErrorPage("Link wygasł", "Link potwierdzenia wygasł. Skontaktuj się z wypożyczalnią."), "text/html; charset=utf-8");

            return Results.Content(RenderConfirmationPage(data, token), "text/html; charset=utf-8");
        }).RequireRateLimiting("api");

        // POST /confirm/{token} — przetwarzanie potwierdzenia
        app.MapPost("/confirm/{token}", [AllowAnonymous] async (
            string token,
            HttpContext httpContext,
            IRentalConfirmationService confirmationService) =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var result = await confirmationService.ProcessConfirmationAsync(token, ip, userAgent);

            if (result.Success)
            {
                var data = await confirmationService.GetConfirmationDataAsync(token);
                return Results.Content(RenderSuccessPage(data), "text/html; charset=utf-8");
            }

            return Results.Content(RenderErrorPage("Błąd potwierdzenia", result.Message), "text/html; charset=utf-8");
        }).RequireRateLimiting("api");

        return app;
    }

    private static string RenderConfirmationPage(ConfirmationPageData data, string token)
    {
        if (data.IsAlreadyConfirmed)
            return RenderSuccessPage(data);

        var itemsHtml = new StringBuilder();
        foreach (var item in data.Items)
        {
            itemsHtml.Append($@"
                <tr>
                    <td>{System.Net.WebUtility.HtmlEncode(item.ProductName)}</td>
                    <td class=""number quantity"">{item.Quantity}</td>
                    <td class=""number"">{item.PricePerDay:F2} zł/dzień</td>
                    <td class=""number"">{item.Subtotal:F2} zł</td>
                </tr>");
        }

        var regulationsSection = "";
        if (!string.IsNullOrEmpty(data.RegulationsText))
        {
            var escapedRegulations = System.Net.WebUtility.HtmlEncode(data.RegulationsText).Replace("\n", "<br>");
            regulationsSection = $@"
                <div style=""margin:24px 0"">
                    <h2 style=""color:#1f2937;margin-bottom:12px;font-size:16px"">📋 Regulamin wypożyczalni</h2>
                    <div style=""max-height:300px;overflow-y:auto;padding:16px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;line-height:1.6;color:#374151"">
                        {escapedRegulations}
                    </div>
                </div>
                <div style=""margin:16px 0"">
                    <label style=""display:flex;align-items:flex-start;gap:10px;cursor:pointer"">
                        <input type=""checkbox"" id=""acceptRegulations"" style=""margin-top:3px;width:18px;height:18px;accent-color:#2563eb"" onchange=""toggleButton()"">
                        <span style=""font-size:14px;color:#374151"">Zapoznałem/am się z regulaminem wypożyczalni i <strong>akceptuję jego warunki</strong></span>
                    </label>
                </div>";
        }

        var startLocal = PolishTimeZone.FromUtc(data.StartDate);
        var endLocal = PolishTimeZone.FromUtc(data.EndDate);
        var encodedCompanyName = System.Net.WebUtility.HtmlEncode(data.CompanyName ?? "SportRental");
        var encodedToken = System.Net.WebUtility.HtmlEncode(token);

        return $@"<!DOCTYPE html>
<html lang=""pl"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>Potwierdzenie wynajmu — {encodedCompanyName}</title>
    <style>
        * {{ margin:0; padding:0; box-sizing:border-box; }}
        body {{ font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif; background:#f3f4f6; color:#1f2937; }}
        .container {{ max-width:600px; margin:0 auto; padding:20px; }}
        .card {{ background:white; border-radius:12px; box-shadow:0 1px 3px rgba(0,0,0,0.1); padding:32px; margin-bottom:16px; }}
        .logo {{ text-align:center; margin-bottom:24px; }}
        .logo h1 {{ font-size:24px; color:#2563eb; }}
        .logo p {{ color:#6b7280; font-size:14px; margin-top:4px; }}
        .info-row {{ display:flex; justify-content:space-between; gap:12px; padding:8px 0; border-bottom:1px solid #f3f4f6; }}
        .info-label {{ color:#6b7280; font-size:14px; }}
        .info-value {{ font-weight:600; font-size:14px; text-align:right; overflow-wrap:anywhere; }}
        .total-row {{ display:flex; justify-content:space-between; padding:12px 0; margin-top:8px; }}
        .total-label {{ font-size:16px; font-weight:700; }}
        .total-value {{ font-size:18px; font-weight:700; color:#2563eb; }}
        table {{ width:100%; table-layout:fixed; border-collapse:collapse; margin:16px 0; }}
        th {{ padding:10px 12px; background:#f9fafb; text-align:left; font-size:13px; color:#6b7280; font-weight:600; border-bottom:2px solid #e5e7eb; }}
        th:first-child {{ width:38%; }}
        th:nth-child(2) {{ width:14%; }}
        th:nth-child(3) {{ width:27%; }}
        th:nth-child(4) {{ width:21%; }}
        td {{ padding:10px 12px; border-bottom:1px solid #e5e7eb; overflow-wrap:anywhere; }}
        td.number {{ text-align:right; }}
        td.quantity {{ text-align:center; }}
        .btn {{ display:block; width:100%; padding:16px; background:#2563eb; color:white; border:none; border-radius:8px; font-size:16px; font-weight:600; cursor:pointer; text-align:center; }}
        .btn:hover {{ background:#1d4ed8; }}
        .btn:disabled {{ background:#9ca3af; cursor:not-allowed; }}
        .footer {{ text-align:center; padding:16px; color:#4b5563; font-size:12px; }}
        @media (max-width:480px) {{
            .container {{ padding:12px; }}
            .card {{ padding:20px; }}
            .logo h1, .footer {{ overflow-wrap:anywhere; }}
            th, td {{ padding:8px 4px; font-size:12px; }}
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <main class=""card"">
            <div class=""logo"">
                <h1>🏂 {encodedCompanyName}</h1>
                <p>Potwierdzenie wynajmu sprzętu</p>
            </div>

            <div style=""background:#eff6ff;border:1px solid #bfdbfe;border-radius:8px;padding:16px;margin-bottom:24px"">
                <p style=""font-size:15px;color:#1e40af"">
                    Cześć <strong>{System.Net.WebUtility.HtmlEncode(data.CustomerName)}</strong>! 
                    Prosimy o potwierdzenie warunków wynajmu.
                </p>
            </div>

            <div style=""margin-bottom:20px"">
                <div class=""info-row"">
                    <span class=""info-label"">Data od</span>
                    <span class=""info-value"">{startLocal:dd.MM.yyyy HH:mm}</span>
                </div>
                <div class=""info-row"">
                    <span class=""info-label"">Data do</span>
                    <span class=""info-value"">{endLocal:dd.MM.yyyy HH:mm}</span>
                </div>
                <div class=""info-row"">
                    <span class=""info-label"">Kaucja</span>
                    <span class=""info-value"">{data.DepositAmount:F2} zł</span>
                </div>
            </div>

            <h2 style=""font-size:15px;color:#374151;margin-bottom:8px"">Wypożyczany sprzęt</h2>
            <table>
                <thead>
                    <tr>
                        <th>Sprzęt</th>
                        <th style=""text-align:center"">Ilość</th>
                        <th style=""text-align:right"">Cena</th>
                        <th style=""text-align:right"">Razem</th>
                    </tr>
                </thead>
                <tbody>
                    {itemsHtml}
                </tbody>
            </table>

            <div class=""total-row"">
                <span class=""total-label"">Do zapłaty</span>
                <span class=""total-value"">{data.TotalAmount:F2} zł</span>
            </div>

            {regulationsSection}

            <form method=""post"" action=""/confirm/{encodedToken}"" id=""confirmForm"" style=""margin-top:24px"">
                <button type=""submit"" class=""btn"" id=""confirmBtn"" {(string.IsNullOrEmpty(data.RegulationsText) ? "" : "disabled")}>
                    ✅ Potwierdzam wynajem
                </button>
            </form>
        </main>

        <footer class=""footer"">
            {(data.CompanyPhone != null ? $"📞 {System.Net.WebUtility.HtmlEncode(data.CompanyPhone)}" : "")}
            {(data.CompanyEmail != null ? $" · ✉️ {System.Net.WebUtility.HtmlEncode(data.CompanyEmail)}" : "")}
            <br>© {DateTime.Now.Year} {encodedCompanyName}
        </footer>
    </div>

    <script>
        function toggleButton() {{
            var cb = document.getElementById('acceptRegulations');
            var btn = document.getElementById('confirmBtn');
            btn.disabled = !cb.checked;
        }}
        document.getElementById('confirmForm').addEventListener('submit', function(e) {{
            var btn = document.getElementById('confirmBtn');
            btn.disabled = true;
            btn.textContent = '⏳ Potwierdzanie...';
        }});
    </script>
</body>
</html>";
    }

    private static string RenderSuccessPage(ConfirmationPageData? data)
    {
        var companyName = data?.CompanyName ?? "SportRental";
        return $@"<!DOCTYPE html>
<html lang=""pl"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>Wynajem potwierdzony — {System.Net.WebUtility.HtmlEncode(companyName)}</title>
    <style>
        * {{ margin:0; padding:0; box-sizing:border-box; }}
        body {{ font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif; background:#f3f4f6; color:#1f2937; display:flex; align-items:center; justify-content:center; min-height:100vh; }}
        .card {{ background:white; border-radius:12px; box-shadow:0 1px 3px rgba(0,0,0,0.1); padding:48px; text-align:center; max-width:480px; margin:20px; }}
        .icon {{ font-size:64px; margin-bottom:16px; }}
        h1 {{ color:#059669; font-size:24px; margin-bottom:8px; }}
        p {{ color:#6b7280; font-size:15px; line-height:1.6; }}
    </style>
</head>
<body>
    <main class=""card"">
        <div class=""icon"">✅</div>
        <h1>Wynajem potwierdzony!</h1>
        <p>Dziękujemy za potwierdzenie warunków wynajmu.</p>
        <p style=""margin-top:12px;color:#374151"">
            Do zobaczenia w <strong>{System.Net.WebUtility.HtmlEncode(companyName)}</strong>!
        </p>
        {(data?.CompanyPhone != null ? $"<p style=\"margin-top:16px;font-size:13px;color:#9ca3af\">W razie pytań: 📞 {System.Net.WebUtility.HtmlEncode(data.CompanyPhone)}</p>" : "")}
    </main>
</body>
</html>";
    }

    private static string RenderErrorPage(string title, string message)
    {
        return $@"<!DOCTYPE html>
<html lang=""pl"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
    <style>
        * {{ margin:0; padding:0; box-sizing:border-box; }}
        body {{ font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif; background:#f3f4f6; color:#1f2937; display:flex; align-items:center; justify-content:center; min-height:100vh; }}
        .card {{ background:white; border-radius:12px; box-shadow:0 1px 3px rgba(0,0,0,0.1); padding:48px; text-align:center; max-width:480px; margin:20px; }}
        .icon {{ font-size:64px; margin-bottom:16px; }}
        h1 {{ color:#dc2626; font-size:24px; margin-bottom:8px; }}
        p {{ color:#6b7280; font-size:15px; line-height:1.6; }}
    </style>
</head>
<body>
    <main class=""card"">
        <div class=""icon"">⚠️</div>
        <h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>
        <p>{System.Net.WebUtility.HtmlEncode(message)}</p>
    </main>
</body>
</html>";
    }
}
