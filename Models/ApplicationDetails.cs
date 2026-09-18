namespace BusinessLicensing_Practice.Models
{
    public class ApplicationDetails
    {
        public int Id { get; set; }

        public int ApplicationId { get; set; }

        public Application? Application { get; set; }

        public string? ApplicationType { get; set; }

        public string? ApplicantName { get; set; }

        public string? ApplicantFirstName { get; set; }
        public string? ApplicantLastName { get; set; }

        public string? ApplicantAddress { get; set; }

        public string? ApplicantAddressLine1 { get; set; }
        public string? ApplicantAddressLine2 { get; set; }
        public string? ApplicantSuburb { get; set; }
        public string? ApplicantCity { get; set; }
        public string? ApplicantPostalCode { get; set; }

        public string? ApplicantTelephone { get; set; }

        public string? ApplicantEmail { get; set; }

        public string? PostalAddress { get; set; }

        public bool? PostalAddressSameAsBusiness { get; set; }
        public string? PostalAddressLine1 { get; set; }
        public string? PostalAddressLine2 { get; set; }
        public string? PostalSuburb { get; set; }
        public string? PostalCity { get; set; }
        public string? PostalPostalCode { get; set; }

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
