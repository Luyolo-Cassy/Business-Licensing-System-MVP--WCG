namespace BusinessLicensing_Practice.Models
{
    public class Application
    {
        public int Id { get; set; }

        public string ApplicationNumber { get; set; } = "";

        public string BusinessName { get; set; } = "";

        public string LicenceType { get; set; } = "";

        public string Status { get; set; } = "";

        public string UploadedDocumentName { get; set; } = "";

        public string UploadedDocumentPath { get; set; } = "";

        public List<ApplicationDocument> Documents { get; set; } = new();

        public string RegistrationNumber { get; set; } = "";

        public string TaxNumber { get; set; } = "";

        public string BusinessAddress { get; set; } = "";

        public string AddressLine1 { get; set; } = "";

        public string AddressLine2 { get; set; } = "";

        public string Suburb { get; set; } = "";

        public string City { get; set; } = "";

        public string PostalCode { get; set; } = "";

        public string TradingName { get; set; } = "";

        public string BusinessCategory { get; set; } = "";

        public string FoodHandlingType { get; set; } = "";

        public string EntertainmentActivityType { get; set; } = "";

        public string ApplicationFormFileName { get; set; } = "";

        public string ApplicationFormFilePath { get; set; } = "";

        public string? Municipality { get; set; }

        public ApplicationDetails? Details { get; set; }

        public bool PopiaConsentAccepted { get; set; }

        public DateTime DateSubmitted { get; set; }

        public string UserId { get; set; } = "";

        public ApplicationUser? User { get; set; }


    }
}
