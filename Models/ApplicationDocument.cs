namespace BusinessLicensing_Practice.Models
{
    public class ApplicationDocument
    {
        public int Id { get; set; }

        public int ApplicationId { get; set; }

        public string DocumentType { get; set; } = "";

        public string FileName { get; set; } = "";

        public string FilePath { get; set; } = "";

        public Application? Application { get; set; }
    }
}