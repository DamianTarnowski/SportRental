namespace SportRental.MediaStorage.Options;

public class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Static API keys allowed to upload/delete files. GET endpoints remain public by default.
    /// </summary>
    public string[] ApiKeys { get; set; } = [];
}
