using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportRental.Admin.Services.Sms;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Chat;

/// <summary>
/// Write tools z DWUSTOPNIOWYM confirmation flow:
///  - confirm=false → zwraca preview ("would do X") + flag awaiting_confirmation=true
///  - confirm=true → faktycznie commitja zmianę
/// Model jest instruowany żeby ZAWSZE najpierw zaproponować preview, czekać na explicit
/// zgodę usera ('tak, zapisz' / 'ok' / 'potwierdzam'), dopiero wtedy wywołać confirm=true.
/// </summary>
public sealed class WriteToolService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ISmsSender _sms;
    private readonly ILogger<WriteToolService> _logger;

    public WriteToolService(IDbContextFactory<ApplicationDbContext> dbFactory, ISmsSender sms, ILogger<WriteToolService> logger)
    {
        _dbFactory = dbFactory;
        _sms = sms;
        _logger = logger;
    }

    /// <summary>
    /// Zaktualizuj notatki klienta. Mode: append (dopisz) / replace (nadpisz).
    /// </summary>
    public async Task<string> UpdateCustomerNotesAsync(
        Guid tenantId, string customerIdOrEmail, string notes, string? mode, bool confirm, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        Customer? customer = null;
        if (Guid.TryParse(customerIdOrEmail, out var cid))
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == cid, ct);
        else
            customer = await db.Customers.FirstOrDefaultAsync(
                c => c.Email == customerIdOrEmail || c.PhoneNumber == customerIdOrEmail, ct);

        if (customer == null)
            return JsonSerializer.Serialize(new { error = "customer_not_found", query = customerIdOrEmail });

        var actualMode = (mode ?? "append").ToLowerInvariant();
        var stamp = $"[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] {notes}";
        var newNotes = actualMode switch
        {
            "replace" => stamp,
            _ => string.IsNullOrWhiteSpace(customer.Notes) ? stamp : customer.Notes + "\n" + stamp
        };

        if (!confirm)
        {
            return JsonSerializer.Serialize(new
            {
                awaiting_confirmation = true,
                action = "update_customer_notes",
                preview = new
                {
                    customer = customer.FullName,
                    customerId = customer.Id,
                    mode = actualMode,
                    currentNotes = customer.Notes ?? "(brak)",
                    newNotesAfterChange = newNotes
                },
                hint = "Pokaż preview użytkownikowi i poproś o explicit zgodę. Wywołaj ponownie z confirm=true gdy potwierdzi."
            });
        }

        customer.Notes = newNotes;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("WriteTool: notes updated for customer {Id} mode={Mode}", customer.Id, actualMode);
        return JsonSerializer.Serialize(new { saved = true, customerId = customer.Id, customer = customer.FullName });
    }

    /// <summary>
    /// Oznacz wynajem jako zwrócony — przejście na status Completed + ReturnedAtUtc.
    /// Tylko z Active/Confirmed/Issued. condition: ok/damaged/lost.
    /// </summary>
    public async Task<string> MarkRentalReturnedAsync(
        Guid tenantId, Guid rentalId, string? condition, string? returnNotes, bool confirm, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var rental = await db.Rentals
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental == null)
            return JsonSerializer.Serialize(new { error = "rental_not_found", rentalId });

        if (rental.Status == RentalStatus.Completed)
            return JsonSerializer.Serialize(new { error = "already_completed", rentalId });

        if (rental.Status == RentalStatus.Cancelled)
            return JsonSerializer.Serialize(new { error = "rental_cancelled", rentalId });

        var c = (condition ?? "ok").ToLowerInvariant();

        if (!confirm)
        {
            return JsonSerializer.Serialize(new
            {
                awaiting_confirmation = true,
                action = "mark_rental_returned",
                preview = new
                {
                    rentalId = rental.Id,
                    customer = rental.Customer?.FullName ?? "(nieznany)",
                    currentStatus = rental.Status.ToString(),
                    plannedEnd = rental.EndDateUtc,
                    condition = c,
                    returnNotes,
                    afterChange = "Status → Completed, ReturnedAtUtc → teraz"
                },
                hint = "Pokaż preview userowi. Po explicit zgodzie wywołaj z confirm=true."
            });
        }

        rental.Status = RentalStatus.Completed;
        rental.ReturnedAtUtc = DateTime.UtcNow;
        rental.ReturnNotes = string.IsNullOrWhiteSpace(returnNotes)
            ? $"Zwrot przez asystenta AI. Stan: {c}."
            : $"Zwrot przez asystenta AI. Stan: {c}. {returnNotes}";
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("WriteTool: rental {Id} marked as returned (condition={Cond})", rentalId, c);

        return JsonSerializer.Serialize(new
        {
            saved = true,
            rentalId,
            newStatus = "Completed",
            returnedAt = rental.ReturnedAtUtc
        });
    }

    /// <summary>
    /// Wyślij ręczny SMS przypomnienia o zwrocie / wydaniu / cokolwiek.
    /// </summary>
    public async Task<string> SendReminderSmsAsync(
        Guid tenantId, Guid rentalId, string? customMessage, bool confirm, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var rental = await db.Rentals
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental == null)
            return JsonSerializer.Serialize(new { error = "rental_not_found", rentalId });

        // NXRE r2 audit: reminder o zwrocie tylko gdy sprzęt FAKTYCZNIE wydany
        // (chat tool nie powinien móc spamować klientów którzy nie odebrali sprzętu).
        if (!rental.IssuedAtUtc.HasValue || rental.ReturnedAtUtc.HasValue)
            return JsonSerializer.Serialize(new {
                error = "reminder_not_applicable",
                reason = !rental.IssuedAtUtc.HasValue ? "rental_not_issued" : "rental_already_returned",
                rentalId
            });

        var phone = rental.Customer?.PhoneNumber;
        if (string.IsNullOrWhiteSpace(phone))
            return JsonSerializer.Serialize(new { error = "no_phone_number", rentalId });

        var fullName = rental.Customer?.FullName ?? "Klient";
        var msg = string.IsNullOrWhiteSpace(customMessage)
            ? $"SportRental: Cześć {fullName.Split(' ')[0]}, przypominamy o zwrocie sprzętu (planowany do {rental.EndDateUtc.ToLocalTime():dd.MM HH:mm})."
            : customMessage.Length > 320 ? customMessage[..320] : customMessage;

        if (!confirm)
        {
            return JsonSerializer.Serialize(new
            {
                awaiting_confirmation = true,
                action = "send_reminder_sms",
                preview = new
                {
                    to = phone,
                    customer = fullName,
                    rentalId,
                    message = msg,
                    chars = msg.Length
                },
                hint = "Pokaż preview userowi. Wyślij dopiero po explicit zgodzie (confirm=true)."
            });
        }

        try
        {
            await _sms.SendReminderAsync(phone, fullName, msg, ct);
            _logger.LogInformation("WriteTool: reminder SMS sent for rental {Id} to {Phone}", rentalId, phone);
            return JsonSerializer.Serialize(new { sent = true, to = phone, rentalId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WriteTool: SMS send failed for {RentalId}", rentalId);
            return JsonSerializer.Serialize(new { error = "sms_send_failed", details = ex.Message });
        }
    }
}
