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

    public static class ApplicationDocumentTypes
    {
        public const string AdditionalSupportingDocument = "Additional Supporting Document";
        private const string AdditionalPrefix = AdditionalSupportingDocument + ":";

        public static string CreateAdditional() => AdditionalPrefix + Guid.NewGuid().ToString("N");

        public static bool IsAdditional(string? documentType) =>
            documentType?.StartsWith(AdditionalPrefix, StringComparison.Ordinal) == true &&
            Guid.TryParseExact(documentType[AdditionalPrefix.Length..], "N", out _);

        public static string DisplayName(string documentType) =>
            IsAdditional(documentType) ? AdditionalSupportingDocument : documentType;
    }
}
