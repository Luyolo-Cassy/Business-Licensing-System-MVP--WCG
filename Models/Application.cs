namespace BusinessLicensing_Practice.Models
{
    public class Application
    {
        public int Id { get; set; }

        public string ApplicationNumber { get; set; } = "";

        public string BusinessName { get; set; } = "";

        public string LicenceType { get; set; } = "";

        public string Status { get; set; } = "";

        public DateTime DateSubmitted { get; set; }
    }
}