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

        // Step 3 physical operating/trading address. These PlaceOfBusiness fields
        // are the address source for future municipal routing.
        public string PlaceOfBusinessAddress { get; set; } = "";

        public string PlaceOfBusinessAddressLine1 { get; set; } = "";

        public string PlaceOfBusinessAddressLine2 { get; set; } = "";

        public string PlaceOfBusinessSuburb { get; set; } = "";

        public string PlaceOfBusinessCity { get; set; } = "";

        public string PlaceOfBusinessPostalCode { get; set; } = "";

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
