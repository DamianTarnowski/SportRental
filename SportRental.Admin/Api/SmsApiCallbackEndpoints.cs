using Microsoft.AspNetCore.Authorization;
using SportRental.Admin.Services.Sms;

namespace SportRental.Admin.Api
{
    /// <summary>
    /// Endpointy dla callbacków SMSAPI (raporty doręczeń)
    /// Dokumentacja: https://www.smsapi.pl/docs#9-raporty-callback
    /// </summary>
    public static class SmsApiCallbackEndpoints
    {
        public static IEndpointRouteBuilder MapSmsApiCallbacks(this IEndpointRouteBuilder app)
        {
            var sms = app.MapGroup("/api/sms")
                .AllowAnonymous(); // SMSAPI wywołuje ten endpoint bez autoryzacji

            // Callback dla raportów doręczeń SMS
            // SMSAPI wysyła POST z danymi w formie query string lub form data
            sms.MapPost("/callback", [AllowAnonymous] async (
                HttpContext context,
                ILogger<SmsDeliveryReport> logger) =>
            {
                var report = new SmsDeliveryReport();

                // SMSAPI może wysłać dane jako query string lub form-data
                if (context.Request.HasFormContentType)
                {
                    var form = await context.Request.ReadFormAsync();
                    report.MsgId = form["MsgId"].FirstOrDefault();
                    report.Status = form["status"].FirstOrDefault();
                    report.To = form["to"].FirstOrDefault();
                    report.DoneDate = form["donedate"].FirstOrDefault();
                    report.Idx = form["idx"].FirstOrDefault();
                    report.Username = form["username"].FirstOrDefault();
                    if (int.TryParse(form["parts"].FirstOrDefault(), out var parts))
                        report.Parts = parts;
                }
                else
                {
                    // Query string fallback
                    var query = context.Request.Query;
                    report.MsgId = query["MsgId"].FirstOrDefault();
                    report.Status = query["status"].FirstOrDefault();
                    report.To = query["to"].FirstOrDefault();
                    report.DoneDate = query["donedate"].FirstOrDefault();
                    report.Idx = query["idx"].FirstOrDefault();
                    report.Username = query["username"].FirstOrDefault();
                    if (int.TryParse(query["parts"].FirstOrDefault(), out var parts))
                        report.Parts = parts;
                }

                // Logowanie raportu doręczenia
                logger.LogInformation(
                    "SMS Delivery Report received - MsgId: {MsgId}, Status: {Status}, To: {To}, DoneDate: {DoneDate}",
                    report.MsgId, report.Status, report.To, report.DoneDate);

                // Tutaj można dodać dodatkową logikę, np.:
                // - Zapisanie statusu do bazy danych
                // - Aktualizacja SmsConfirmation
                // - Wysłanie notyfikacji

                // SMSAPI oczekuje odpowiedzi 200 OK
                return Results.Ok();
            });

            // GET endpoint dla testowania/weryfikacji
            sms.MapGet("/callback", [AllowAnonymous] () =>
            {
                return Results.Ok(new { status = "SMSAPI callback endpoint active" });
            });

            return app;
        }
    }
}

