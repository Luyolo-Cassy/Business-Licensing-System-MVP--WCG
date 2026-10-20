namespace BusinessLicensing_Practice.Models;

// Stores administrator-controlled AI feature flags in a single seeded row.
public class AiSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool DocumentValidationEnabled { get; set; }
    public bool CertificateOfIncorporationValidationEnabled { get; set; }
    public bool ProofOfAddressValidationEnabled { get; set; }
    public bool ApplicationSummariesEnabled { get; set; }
}
