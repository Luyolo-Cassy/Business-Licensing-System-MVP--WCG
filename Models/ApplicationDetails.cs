namespace BusinessLicensing_Practice.Models
{
    public class ApplicationDetails
    {
        public int Id { get; set; }

        public int ApplicationId { get; set; }

        public Application? Application { get; set; }

        public string? ApplicationType { get; set; }

        public string? ApplicantName { get; set; }

        public string? ApplicantAddress { get; set; }

        public string? ApplicantTelephone { get; set; }

        public string? ApplicantEmail { get; set; }

        public string? PostalAddress { get; set; }

        public string? ContactPerson { get; set; }

        public string? BusinessTelephone { get; set; }

        public string? BusinessEmail { get; set; }

        public string? ErfNumber { get; set; }

        public string? Zoning { get; set; }

        public string? TradingHours { get; set; }

        public string? LicenceSpecificDetailsJson { get; set; }

        public bool? DeclarationAccepted { get; set; }

        public DateTime? DeclarationAcceptedAt { get; set; }
    }
}
