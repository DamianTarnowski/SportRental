namespace SportRental.Admin.Services.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "SportRental";
    public string Audience { get; set; } = "SportRental.Client";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
    public int GuestTokenLifetimeHours { get; set; } = 48;
}
