namespace SportRental.Shared.Models;

public class TenantLocationDto
{
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Voivodeship { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? OpeningHours { get; set; }
    public string? LogoUrl { get; set; }
    public int ProductCount { get; set; }
    public double? Distance { get; set; }
}
