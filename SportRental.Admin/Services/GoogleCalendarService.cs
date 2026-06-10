using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services;

/// <summary>
/// Faza 9c — sync wynajmów do Google Calendar partnera.
/// Flow OAuth: admin idzie na /admin/google-calendar/connect → redirect do Google →
/// callback /signin-google-calendar zapisuje refresh_token w GoogleCalendarConfig.
/// Sync: SyncRentalAsync wywołane po Create/Update/Return (background) — creates event.
///
/// Wykorzystuje istniejący Google OAuth client (GoogleOAuth--ClientId/Secret w Key Vault).
/// Scope dodatkowy: https://www.googleapis.com/auth/calendar.events
/// </summary>
public interface IGoogleCalendarService
{
    /// <summary>Wymienia OAuth authorization code na refresh_token i zapisuje per tenant.</summary>
    Task ConnectTenantAsync(Guid tenantId, string authorizationCode, string redirectUri, CancellationToken ct = default);

    Task<GoogleCalendarConfig?> GetConfigAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Disconnect — usuwa config + revoke token (best-effort).</summary>
    Task DisconnectTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Create / update event w kalendarzu partnera dla danego rentala. Idempotent przez GoogleCalendarEvent map.</summary>
    Task SyncRentalAsync(Guid rentalId, CancellationToken ct = default);

    string BuildAuthorizationUrl(string redirectUri, Guid tenantId);
}

public sealed class GoogleCalendarService : IGoogleCalendarService
{
    private const string CalendarScope = "https://www.googleapis.com/auth/calendar.events";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string RevokeUrl = "https://oauth2.googleapis.com/revoke";

    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleCalendarService> _logger;

    public GoogleCalendarService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<GoogleCalendarService> logger)
    {
        _dbFactory = dbFactory;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private (string clientId, string clientSecret) GetClientCreds()
    {
        var id = _config["GoogleOAuth:ClientId"] ?? _config["Google:ClientId"];
        var secret = _config["GoogleOAuth:ClientSecret"] ?? _config["Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Brak GoogleOAuth credentials w konfiguracji.");
        return (id, secret);
    }

    public string BuildAuthorizationUrl(string redirectUri, Guid tenantId)
    {
        var (clientId, _) = GetClientCreds();
        var scope = Uri.EscapeDataString($"openid email {CalendarScope}");
        var redir = Uri.EscapeDataString(redirectUri);
        var state = Uri.EscapeDataString(tenantId.ToString("N"));
        return "https://accounts.google.com/o/oauth2/v2/auth"
            + $"?client_id={clientId}"
            + $"&redirect_uri={redir}"
            + "&response_type=code"
            + $"&scope={scope}"
            + "&access_type=offline"
            + "&prompt=consent"
            + $"&state={state}";
    }

    public async Task ConnectTenantAsync(Guid tenantId, string authorizationCode, string redirectUri, CancellationToken ct = default)
    {
        var (clientId, clientSecret) = GetClientCreds();
        var http = _httpFactory.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = authorizationCode,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        });

        var resp = await http.PostAsync(TokenUrl, form, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed: {(int)resp.StatusCode} {json}");

        var token = JsonSerializer.Deserialize<TokenResponse>(json)
            ?? throw new InvalidOperationException("Token response empty.");
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException("Brak refresh_token w odpowiedzi — prawdopodobnie konto już zatwierdziło scope (potrzebny prompt=consent).");

        // Pobierz email konta dla wyświetlenia w UI
        var email = await FetchUserEmail(http, token.AccessToken ?? string.Empty, ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.GoogleCalendarConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        if (existing != null)
        {
            existing.RefreshToken = token.RefreshToken;
            existing.ConnectedEmail = email;
            existing.IsActive = true;
            existing.CalendarId = existing.CalendarId; // zachowaj
        }
        else
        {
            db.GoogleCalendarConfigs.Add(new GoogleCalendarConfig
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RefreshToken = token.RefreshToken,
                ConnectedEmail = email,
                CalendarId = "primary",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Google Calendar connected for tenant {TenantId} (email {Email})", tenantId, email);
    }

    public async Task<GoogleCalendarConfig?> GetConfigAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.GoogleCalendarConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
    }

    public async Task DisconnectTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cfg = await db.GoogleCalendarConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        if (cfg == null) return;

        // Best-effort revoke token
        try
        {
            var http = _httpFactory.CreateClient();
            await http.PostAsync(RevokeUrl + "?token=" + Uri.EscapeDataString(cfg.RefreshToken), null, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Revoke token failed (continuing)"); }

        // Usuń mapping eventów (events w Google zostaną — nie kasujemy historii)
        db.GoogleCalendarConfigs.Remove(cfg);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Google Calendar disconnected for tenant {TenantId}", tenantId);
    }

    public async Task SyncRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rental = await db.Rentals
            .IgnoreQueryFilters()
            .Include(r => r.Customer)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);
        if (rental == null) return;

        var cfg = await db.GoogleCalendarConfigs.FirstOrDefaultAsync(c => c.TenantId == rental.TenantId, ct);
        if (cfg == null || !cfg.IsActive) return;

        var accessToken = await GetAccessTokenAsync(cfg.RefreshToken, ct);
        if (accessToken == null)
        {
            _logger.LogWarning("Nie udało się pobrać access_token dla tenant {TenantId}", rental.TenantId);
            return;
        }

        var existing = await db.GoogleCalendarEvents
            .FirstOrDefaultAsync(e => e.TenantId == rental.TenantId && e.RentalId == rentalId, ct);

        var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var eventBody = new
        {
            summary = $"Wynajem · {rental.Customer?.FullName ?? "Klient"}",
            description = BuildDescription(rental),
            start = new { dateTime = rental.StartDateUtc.ToString("yyyy-MM-ddTHH:mm:ssZ") },
            end = new { dateTime = rental.EndDateUtc.ToString("yyyy-MM-ddTHH:mm:ssZ") },
            colorId = "9" // Blueberry — neutral; mogłoby być per status
        };

        var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(cfg.CalendarId)}/events";
        HttpResponseMessage resp;
        if (existing == null)
        {
            resp = await http.PostAsJsonAsync(url, eventBody, ct);
        }
        else
        {
            resp = await http.PutAsJsonAsync($"{url}/{existing.EventId}", eventBody, ct);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Google Calendar sync failed for rental {RentalId}: {Code} {Err}", rentalId, (int)resp.StatusCode, err);
            return;
        }

        var resultJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(resultJson);
        var eventId = doc.RootElement.GetProperty("id").GetString() ?? string.Empty;

        if (existing == null)
        {
            db.GoogleCalendarEvents.Add(new GoogleCalendarEvent
            {
                Id = Guid.NewGuid(),
                TenantId = rental.TenantId,
                RentalId = rentalId,
                EventId = eventId,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        cfg.LastSyncAtUtc = DateTime.UtcNow;
        cfg.SyncedCount++;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Google Calendar event {EventId} synced for rental {RentalId}", eventId, rentalId);
    }

    private static string BuildDescription(Rental rental)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(rental.Customer?.PhoneNumber))
            lines.Add($"Tel.: {rental.Customer.PhoneNumber}");
        if (!string.IsNullOrWhiteSpace(rental.Customer?.Email))
            lines.Add($"Email: {rental.Customer.Email}");
        lines.Add($"Status: {rental.Status}");
        lines.Add($"Kwota: {rental.TotalAmount:N2} zł");
        if (rental.Items?.Any() == true)
        {
            lines.Add("");
            lines.Add("Sprzęt:");
            foreach (var it in rental.Items)
                lines.Add($"• {it.Quantity}× (ProductId {it.ProductId.ToString()[..8]})");
        }
        if (!string.IsNullOrWhiteSpace(rental.Notes))
        {
            lines.Add("");
            lines.Add($"Notatki: {rental.Notes}");
        }
        return string.Join("\n", lines);
    }

    private async Task<string?> GetAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        var (clientId, clientSecret) = GetClientCreds();
        var http = _httpFactory.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token"
        });
        var resp = await http.PostAsync(TokenUrl, form, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct);
        var token = JsonSerializer.Deserialize<TokenResponse>(json);
        return token?.AccessToken;
    }

    private static async Task<string?> FetchUserEmail(HttpClient http, string accessToken, CancellationToken ct)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    }
}
