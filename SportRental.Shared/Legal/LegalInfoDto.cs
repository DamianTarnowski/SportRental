namespace SportRental.Shared.Legal;

/// <summary>
/// Wyłącznie publiczne dane identyfikacyjne operatora platformy.
/// Nie należy umieszczać w tym kontrakcie sekretów ani danych konfiguracyjnych usług.
/// </summary>
public sealed record LegalInfoDto
{
    public string ServiceName { get; init; } = "RentSpot";
    public string? OperatorName { get; init; }
    public string? OperatorAddress { get; init; }
    public string? OperatorNip { get; init; }
    public string? OperatorKrs { get; init; }
    public string? OperatorEmail { get; init; }
    public string? OperatorPhone { get; init; }
    public string? ComplaintsEmail { get; init; }
    public string? PrivacyEmail { get; init; }
    public string TermsVersion { get; init; } = LegalDocumentVersions.Terms;
    public string PrivacyVersion { get; init; } = LegalDocumentVersions.Privacy;
    public DateTime EffectiveFromUtc { get; init; } = LegalDocumentVersions.EffectiveFromUtc;
    public bool IsOperatorDataComplete { get; init; }
}
