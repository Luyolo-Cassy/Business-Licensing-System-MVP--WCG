using BusinessLicensing_Practice.Fonts;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using BusinessLicensing_Practice.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

namespace BusinessLicensingSystem.Tests.PdfGenerationAndStorage;

[CollectionDefinition("PDF workflows", DisableParallelization = true)]
public sealed class PdfWorkflowCollection;

[Collection("PDF workflows")]
[Trait("Feature", "PDF Generation and Storage")]
public sealed class PdfGenerationAndStorageTests
{
    [Fact]
    public void GeneratedApplicationPdf_IsValidAndReadable()
    {
        PdfTestSupport.EnsureFontResolver();
        var bytes = new ApplicationPdfService().Generate(ApplicationFixture());

        using var pdf = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.True(bytes.Length > 4);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.True(pdf.PageCount >= 1);
    }

    [Fact]
    public void GeneratedApplicationPdf_ContainsReferenceMunicipalityAndApplicantInformation()
    {
        PdfTestSupport.EnsureFontResolver();
        using var pdf = PdfReader.Open(new MemoryStream(new ApplicationPdfService().Generate(ApplicationFixture())),
            PdfDocumentOpenMode.Import);
        var text = string.Concat(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .SelectMany(page => PdfStrings(ContentReader.ReadContent(page))));

        Assert.Contains("APP-2026-042", text);
        Assert.Contains("Bergrivier Municipality", text);
        Assert.Contains("Ada Lovelace", text);
    }

    [Fact]
    public async Task GeneratedApplicationPdf_CanBeSavedAndReadBackFromIsolatedStorage()
    {
        PdfTestSupport.EnsureFontResolver();
        var root = Path.Combine(Path.GetTempPath(), $"pdf-storage-xunit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var files = new ApplicationFileService(new LocalPrivateFileStore(new TestEnvironment(root)));
            var expected = new ApplicationPdfService().Generate(ApplicationFixture());
            var reference = await files.SaveGeneratedPdfAsync(42, "APP-2026-042-Official-Application-r1.pdf", expected);

            Assert.True(await files.GeneratedPdfExistsAsync(reference));
            var stored = await files.OpenGeneratedPdfAsync(reference);
            Assert.NotNull(stored);
            await using var stream = stored!.Content;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            Assert.Equal(expected, buffer.ToArray());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static Application ApplicationFixture() => new()
    {
        ApplicationNumber = "APP-2026-042", BusinessName = "Lovelace Foods", RegistrationNumber = "2026/123456/07",
        TaxNumber = "0123456789", BusinessCategory = "Food", LicenceType = "Sale of Meals Licence",
        Municipality = "Bergrivier Municipality", Status = ApplicationWorkflow.Submitted, DateSubmitted = DateTime.UtcNow,
        PlaceOfBusinessAddressLine1 = "12 Main Street", PlaceOfBusinessSuburb = "Central",
        PlaceOfBusinessCity = "Piketberg", PlaceOfBusinessPostalCode = "7320", PopiaConsentAccepted = true,
        Details = new ApplicationDetails
        {
            ApplicationType = "New application", ApplicantFirstName = "Ada", ApplicantLastName = "Lovelace",
            ApplicantAddressLine1 = "1 Home Road", ApplicantSuburb = "Central", ApplicantCity = "Piketberg",
            ApplicantPostalCode = "7320", ApplicantTelephone = "0211234567", ApplicantEmail = "ada@example.test",
            PostalAddressSameAsBusiness = true, ContactPerson = "Ada Lovelace", BusinessTelephone = "0211234567",
            BusinessEmail = "business@example.test",
            TradingHours = ApplicationEntry.SerializeTradingHours(ApplicationEntry.Days.Select(day =>
                new TradingDay { Day = day, IsOpen = false }).ToList()),
            OpenOnPublicHolidays = false, DeclarationAccepted = true,
            LicenceSpecificDetailsJson = "{\"foodDescription\":\"Meals\",\"foodHandling\":\"Pre-packed\",\"tradingInformation\":\"Counter service\"}"
        }
    };

    private static IEnumerable<string> PdfStrings(CSequence sequence)
    {
        foreach (var item in sequence)
        {
            if (item is CString text) yield return text.Value;
            if (item is COperator operation)
                foreach (var value in PdfStrings(operation.Operands)) yield return value;
            if (item is CSequence nested)
                foreach (var value in PdfStrings(nested)) yield return value;
        }
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "BusinessLicensingSystem.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }
}

internal static class PdfTestSupport
{
    private static readonly object Gate = new();
    public static void EnsureFontResolver()
    {
        lock (Gate)
        {
            GlobalFontSettings.FontResolver ??= new LiberationSansFontResolver();
        }
    }
}
