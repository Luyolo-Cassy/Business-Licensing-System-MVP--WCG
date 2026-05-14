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

        public DateTime DateSubmitted { get; set; }
    }
}