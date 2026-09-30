using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using System.Text.Json;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"Failed: {name}");
    checks++;
}

Check(ApplicationEntry.ValidRegistration("2024/123456/07"), "registration accepts expected format");
Check(ApplicationEntry.ValidateName("", "Lovelace") == "First Name is required." && ApplicationEntry.ValidateName("Ada", "") == "Last Name is required.", "first and last names required");
Check(ApplicationEntry.ValidateName("Ada", "Lovelace") == null, "valid name accepted");
Check(!ApplicationEntry.ValidRegistration("2024-123456-07") && !ApplicationEntry.ValidRegistration("arbitrary"), "registration rejects invalid format");
Check(ApplicationEntry.ValidTaxNumber("0123456789"), "tax preserves leading zero");
Check(!ApplicationEntry.ValidTaxNumber("123456789") && !ApplicationEntry.ValidTaxNumber("012345678A"), "tax rejects invalid values");

foreach (var email in new[] { "name@example.com", "name@example.co.za", "accounts+west@example.com" })
    Check(ApplicationEntry.ValidEmail(email), $"email accepts {email}");
foreach (var email in new[] { "Cabral.ale", "name@", "example.com", "", "   ", new string('a', ApplicationEntry.MaxEmailLength) + "@example.com" })
    Check(!ApplicationEntry.ValidEmail(email), $"email rejects invalid value with length {email.Length}");

foreach (var telephone in new[] { "021 123 4567", "082 123 4567", "+27 82 123 4567", "(021) 123 4567" })
    Check(ApplicationEntry.ValidTelephone(telephone), $"telephone accepts {telephone}");
foreach (var telephone in new[] { "08184", "phone", "++27 82 123 4567", "082 ABC 4567", "123456", "1234567890123456" })
    Check(!ApplicationEntry.ValidTelephone(telephone), $"telephone rejects {telephone}");

foreach (var postalCode in new[] { "8001", "7708", "7280" })
    Check(ApplicationEntry.ValidPostalCode(postalCode), $"postal code accepts {postalCode}");
foreach (var postalCode in new[] { "80A1", "123", "12345", "", "   " })
    Check(!ApplicationEntry.ValidPostalCode(postalCode), $"postal code rejects invalid value '{postalCode}'");

foreach (var applicationType in ApplicationEntry.ApplicationTypes)
    Check(ApplicationEntry.ValidApplicationType(applicationType), $"application type accepts {applicationType}");
Check(!ApplicationEntry.ValidApplicationType("") && !ApplicationEntry.ValidApplicationType("Other") && !ApplicationEntry.ValidApplicationType(" Renewal "),
    "application type rejects blank, unsupported and altered values");

Check(ApplicationEntry.ValidLicenceAnswer("Yes", true, ["Yes", "No"]), "Yes/No accepts configured answer");
Check(!ApplicationEntry.ValidLicenceAnswer("Banana", true, ["Yes", "No"]), "Yes/No rejects arbitrary answer");
Check(ApplicationEntry.ValidLicenceAnswer("Prepared on premises", true, ["Pre-packed", "Prepared on premises"]), "choice accepts configured answer");
Check(!ApplicationEntry.ValidLicenceAnswer("Elsewhere", true, ["Pre-packed", "Prepared on premises"]), "choice rejects arbitrary answer");
Check(ApplicationEntry.ValidLicenceAnswer("Flexible free text: 123 / details", true, []), "free-text answer remains flexible");
Check(!ApplicationEntry.ValidLicenceAnswer("   ", true, []), "required blank answer remains invalid");

var days = ApplicationEntry.Days.Select(name => new TradingDay { Day = name }).ToList();
Check(ApplicationEntry.ValidateTradingHours(days) != null, "each day needs an explicit status");
foreach (var day in days) day.IsOpen = false;
Check(ApplicationEntry.ValidateTradingHours(days) == null, "closed days need no times");
days[0].IsOpen = true;
Check(ApplicationEntry.ValidateTradingHours(days)?.Contains("both opening and closing") == true, "open day needs both times");
days[0].OpeningTime = "08:00";
Check(ApplicationEntry.ValidateTradingHours(days)?.Contains("both opening and closing") == true, "missing closing time rejected");
days[0].ClosingTime = "17:00";
Check(ApplicationEntry.ValidateTradingHours(days) == null, "typed Monday 08:00 to 17:00 accepted");
days[1].IsOpen = true; days[1].OpeningTime = "08:00"; days[1].ClosingTime = "17:00";
Check(ApplicationEntry.ValidateTradingHours(days) == null, "typed Tuesday 08:00 to 17:00 accepted");
days[1].OpeningTime = "8:00";
Check(ApplicationEntry.ValidateTradingHours(days)?.Contains("Tuesday") == true, "single digit hour rejected");
days[1].OpeningTime = "24:00";
Check(ApplicationEntry.ValidateTradingHours(days)?.Contains("HH:mm") == true, "hour outside 24-hour range rejected");
days[1].OpeningTime = "08:60";
Check(ApplicationEntry.ValidateTradingHours(days)?.Contains("HH:mm") == true, "minute outside range rejected");
days[1].OpeningTime = "late";
Check(ApplicationEntry.ValidateTradingHours(days)?.Contains("HH:mm") == true, "arbitrary text rejected");
days[1].OpeningTime = "08:00";
Check(ApplicationEntry.ValidateTradingHours(days) == null, "valid Tuesday time restored");
days[0].ClosingTime = "08:00";
Check(ApplicationEntry.ValidateTradingHours(days)?.Contains("Closing Time") == true, "equal opening and closing times rejected");
days[0].OpeningTime = "17:00"; days[0].ClosingTime = "08:00";
Check(ApplicationEntry.ValidateTradingHours(days)?.Contains("Monday") == true, "closing must follow opening");
days[0].OpeningTime = "08:00"; days[0].ClosingTime = "17:00";
Check(ApplicationEntry.ValidateTradingHours(days) == null, "valid weekly hours");
days[2].OpeningTime = "bad"; days[2].ClosingTime = "bad";
Check(ApplicationEntry.ValidateTradingHours(days) == null, "closed day ignores hidden times");
var hours = ApplicationEntry.SerializeTradingHours(days);
Check(hours == JsonSerializer.Serialize(days), "seven-day trading hours JSON remains unchanged");
Check(ApplicationEntry.FormatTradingHours(hours).Contains("Monday: 08:00 – 17:00") &&
    ApplicationEntry.FormatTradingHours(hours).Contains("Sunday: Closed"), "weekly hours format");
Check(ApplicationEntry.FormatTradingHours("8 till late") == "8 till late", "legacy hours fallback");
Check(ApplicationEntry.ValidatePublicHolidayTrading(null) == "Please indicate whether the business is open on public holidays.",
    "public holiday selection is required");
Check(ApplicationEntry.ValidatePublicHolidayTrading(true) == null && ApplicationEntry.ValidatePublicHolidayTrading(false) == null,
    "explicit public holiday Yes and No selections are accepted");
Check(ApplicationEntry.FormatPublicHolidayTrading(true) == "Yes", "public holiday Yes formatting");
Check(ApplicationEntry.FormatPublicHolidayTrading(false) == "No", "public holiday No formatting");
Check(ApplicationEntry.FormatPublicHolidayTrading(null) == "Not recorded", "historical public holiday value is not inferred as No");
Check(ApplicationEntry.ValidatePostalAddress(null, "", "", "", "") != null, "postal choice required");
Check(ApplicationEntry.ValidatePostalAddress(false, "", "Central", "Cape Town", "8000") != null, "manual postal fields required");
Check(ApplicationEntry.ValidatePostalAddress(true, "", "", "", "") == null, "same as business needs no duplicate fields");

var standardDocumentNames = new[] { "Certificate of Incorporation", "Proof of Address", "Tax Clearance Certificate", "Owner ID Document" };
foreach (var licence in LicenceApplicationCatalog.Licences)
{
    Check(licence.Documents.Select(document => document.DocumentType).SequenceEqual(standardDocumentNames), $"{licence.Name} has the expected supporting documents without CoA");
    Check(licence.Documents.All(document => document.Required), $"{licence.Name} supporting documents remain required");
}

var gaming = LicenceApplicationCatalog.Licences.Single(licence => licence.Id == "gaming-amusement");
var adult = LicenceApplicationCatalog.Licences.Single(licence => licence.Id == "adult-premises-escort-services");
var hawker = LicenceApplicationCatalog.Licences.Single(licence => licence.Id == "hawker-street-trading");
var entertainment = LicenceApplicationCatalog.Licences.Single(licence => licence.Id == "entertainment-venue");
Check(gaming.Questions.All(question => question.Key != "operatingTimes"), "gaming no longer asks for duplicate operating times");
Check(adult.Questions.All(question => question.Key != "operatingTimes"), "adult premises no longer asks for duplicate operating times");
Check(hawker.Questions.All(question => question.Key != "tradingTimes"), "hawker no longer asks for duplicate trading days and hours");
Check(entertainment.Questions.Any(question => question.Key == "performanceTimes" && question.Label == "Performance and entertainment times"),
    "entertainment performance times remains a current question");

var historicalGamingRows = ApplicationPdfService.GetLicenceSpecificRows(
    gaming.Name, "{\"activityType\":\"Arcade\",\"operatingTimes\":\"Daily 09:00 to 18:00\"}");
var historicalHawkerRows = ApplicationPdfService.GetLicenceSpecificRows(
    hawker.Name, "{\"goodsSold\":\"Fruit\",\"tradingTimes\":\"Weekdays 08:00 to 16:00\"}");
Check(historicalGamingRows.Any(row => row.Label == "Operating times" && row.Value == "Daily 09:00 to 18:00"),
    "historical operatingTimes remains readable with its label");
Check(historicalHawkerRows.Any(row => row.Label == "Trading days and hours" && row.Value == "Weekdays 08:00 to 16:00"),
    "historical tradingTimes remains readable with its label");
Check(ApplicationPdfService.GetLicenceSpecificLabel(gaming.Name, "operatingTimes") == "Operating times" &&
    ApplicationPdfService.GetLicenceSpecificLabel(hawker.Name, "tradingTimes") == "Trading days and hours",
    "historical admin labels remain human-readable");

var details = new ApplicationDetails
{
    ApplicantFirstName = "Ada", ApplicantLastName = "Lovelace", ApplicantAddressLine1 = "1 Main Road",
    ApplicantSuburb = "Gardens", ApplicantCity = "Cape Town", ApplicantPostalCode = "8001",
    PostalAddressSameAsBusiness = true, PostalAddressLine1 = "stale hidden address", TradingHours = hours,
    OpenOnPublicHolidays = true
};
var application = new Application
{
    ApplicationNumber = "APP-TEST-001", BusinessName = "Example Business", RegistrationNumber = "2024/123456/07",
    TaxNumber = "0123456789", PlaceOfBusinessAddressLine1 = "2 Market Street", PlaceOfBusinessSuburb = "CBD",
    PlaceOfBusinessCity = "Cape Town", PlaceOfBusinessPostalCode = "8000", Details = details
};
application.Documents.Add(new ApplicationDocument { DocumentType = "Certificate of Acceptability Application", FileName = "legacy-coa.pdf", FilePath = "/uploads/legacy-coa.pdf" });
application.Documents.Add(new ApplicationDocument { DocumentType = "Proof of Soundproofing", FileName = "legacy-soundproofing.pdf", FilePath = "/uploads/legacy-soundproofing.pdf" });
var additionalTypeOne = ApplicationDocumentTypes.CreateAdditional();
var additionalTypeTwo = ApplicationDocumentTypes.CreateAdditional();
application.Documents.Add(new ApplicationDocument { DocumentType = additionalTypeOne, FileName = "municipal-request.pdf", FilePath = "/uploads/municipal-request.pdf" });
application.Documents.Add(new ApplicationDocument { DocumentType = additionalTypeTwo, FileName = "supporting-photo.jpg", FilePath = "/uploads/supporting-photo.jpg" });
Check(additionalTypeOne != additionalTypeTwo && ApplicationDocumentTypes.IsAdditional(additionalTypeOne) &&
    ApplicationDocumentTypes.DisplayName(additionalTypeOne) == "Additional Supporting Document",
    "multiple additional supporting documents receive unique internal keys and a shared display label");
Check(ApplicationEntry.FullName(details) == "Ada Lovelace", "full applicant name");
Check(ApplicationEntry.ApplicantAddress(details) == "1 Main Road, Gardens, Cape Town, 8001", "optional address line omitted");
Check(ApplicationEntry.PostalAddress(application) == "2 Market Street, CBD, Cape Town, 8000", "same as business ignores stale postal fields");
details.PostalAddressSameAsBusiness = false;
details.PostalAddressLine1 = "PO Box 12"; details.PostalSuburb = "Central"; details.PostalCity = "Cape Town"; details.PostalPostalCode = "8000";
Check(ApplicationEntry.PostalAddress(application) == "PO Box 12, Central, Cape Town, 8000", "separate postal address");
Check(ApplicationEntry.FullName(new ApplicationDetails { ApplicantName = "Legacy Name" }) == "Legacy Name", "legacy name fallback");
Check(ApplicationEntry.ApplicantAddress(new ApplicationDetails { ApplicantAddress = "Legacy Address" }) == "Legacy Address", "legacy address fallback");
Check(ApplicationEntry.PostalAddress(new Application { Details = new ApplicationDetails { PostalAddress = "Legacy Postal" } }) == "Legacy Postal", "legacy postal fallback");

var database = Path.Combine(Path.GetTempPath(), $"application-entry-{Guid.NewGuid():N}.db");
try
{
    var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={database}").Options;
    await using (var db = new ApplicationDbContext(options))
    {
        await db.GetService<IMigrator>().MigrateAsync("20260915091722_AddMunicipalityRoutingName");
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO ApplicationDetails (Id, ApplicationId, ApplicantName, ApplicantAddress, PostalAddress, TradingHours) VALUES (9001, 9001, 'Legacy Name', 'Legacy Address', 'Legacy Postal', '9 to 5')");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON");
        await db.Database.CloseConnectionAsync();
        await db.Database.MigrateAsync();
        var old = await db.ApplicationDetails.AsNoTracking().SingleAsync(item => item.Id == 9001);
        Check(ApplicationEntry.FullName(old) == "Legacy Name" && ApplicationEntry.ApplicantAddress(old) == "Legacy Address" &&
            old.TradingHours == "9 to 5" && old.OpenOnPublicHolidays == null, "migration preserves legacy values with public holiday status not recorded");
    }

    await using (var db = new ApplicationDbContext(options))
    {
        var owner = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "entry-test", NormalizedUserName = "ENTRY-TEST", FullName = "Ada Lovelace" };
        db.Users.Add(owner);
        application.UserId = owner.Id;
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        var saved = await db.Applications.AsNoTracking().Include(item => item.Details).Include(item => item.Documents).SingleAsync(item => item.Id == application.Id);
        Check(saved.TaxNumber == "0123456789" && saved.Details?.ApplicantFirstName == "Ada" && saved.Details.ApplicantAddressLine2 == null, "structured applicant and tax persistence");
        Check(await db.ApplicationDocuments.CountAsync(document => document.ApplicationId == saved.Id &&
            (document.DocumentType == "Certificate of Acceptability Application" || document.DocumentType == "Proof of Soundproofing")) == 2,
            "legacy supporting document labels remain stored");
        Check(saved.Documents.Count(document => ApplicationDocumentTypes.IsAdditional(document.DocumentType)) == 2,
            "multiple additional supporting documents persist on one application");
        Check(saved.Details?.PostalAddressSameAsBusiness == false && ApplicationEntry.PostalAddress(saved).StartsWith("PO Box 12"), "separate postal persistence");
        Check(ApplicationEntry.FormatTradingHours(saved.Details!.TradingHours).Contains("Sunday: Closed"), "trading hours persistence");
        Check(saved.Details.OpenOnPublicHolidays == true, "public holiday Yes persists");
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        using var pdf = PdfReader.Open(new MemoryStream(new ApplicationPdfService().Generate(saved)), PdfDocumentOpenMode.Import);
        Check(pdf.PageCount >= 1, "official PDF generated with structured entry");
        static IEnumerable<string> PdfStrings(CSequence sequence)
        {
            foreach (var item in sequence)
            {
                if (item is CString textOperand) yield return textOperand.Value;
                if (item is COperator operation)
                    foreach (var part in PdfStrings(operation.Operands)) yield return part;
                if (item is CSequence nested)
                    foreach (var part in PdfStrings(nested)) yield return part;
            }
        }
        var pdfText = string.Concat(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .SelectMany(page => PdfStrings(ContentReader.ReadContent(page))));
        Check(pdfText.Contains("Ada Lovelace") && pdfText.Contains("1 Main Road") && pdfText.Contains("PO Box 12"), "PDF includes applicant and structured addresses");
        Check(pdfText.Contains("2024/123456/07") && pdfText.Contains("0123456789") && pdfText.Contains("Monday"), "PDF includes registration, tax and daily trading hours");
        Check(pdfText.Contains("Open on public holidays") && pdfText.Contains("Yes"), "PDF includes public holiday Yes");
        Check(pdfText.Contains("legacy-coa.pdf") && pdfText.Contains("legacy-soundproofing.pdf"), "PDF includes legacy supporting documents");
        Check(pdfText.Contains("Additional Supporting Document") && pdfText.Contains("municipal-request.pdf") && pdfText.Contains("supporting-photo.jpg"),
            "PDF includes multiple additional supporting-document filenames under the friendly label");

        saved.Details.OpenOnPublicHolidays = false;
        db.ChangeTracker.Clear();
        db.ApplicationDetails.Update(saved.Details);
        await db.SaveChangesAsync();
        db.Entry(saved.Details).State = EntityState.Detached;
        Check((await db.ApplicationDetails.AsNoTracking().SingleAsync(item => item.Id == saved.Details.Id)).OpenOnPublicHolidays == false,
            "public holiday No persists");
        using var noHolidayPdf = PdfReader.Open(new MemoryStream(new ApplicationPdfService().Generate(saved)), PdfDocumentOpenMode.Import);
        var noHolidayPdfText = string.Concat(noHolidayPdf.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .SelectMany(page => PdfStrings(ContentReader.ReadContent(page))));
        Check(noHolidayPdfText.Contains("Open on public holidays") && noHolidayPdfText.Contains("No"), "PDF includes public holiday No");

        saved.Details.OpenOnPublicHolidays = null;
        using var historicalHolidayPdf = PdfReader.Open(new MemoryStream(new ApplicationPdfService().Generate(saved)), PdfDocumentOpenMode.Import);
        var historicalHolidayPdfText = string.Concat(historicalHolidayPdf.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .SelectMany(page => PdfStrings(ContentReader.ReadContent(page))));
        Check(historicalHolidayPdfText.Contains("Not recorded"), "historical PDF identifies missing public holiday value");

        saved.LicenceType = gaming.Name;
        saved.Details.LicenceSpecificDetailsJson = "{\"activityType\":\"Arcade\",\"operatingTimes\":\"Daily 09:00 to 18:00\"}";
        using var historicalPdf = PdfReader.Open(new MemoryStream(new ApplicationPdfService().Generate(saved)), PdfDocumentOpenMode.Import);
        var historicalPdfText = string.Concat(historicalPdf.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .SelectMany(page => PdfStrings(ContentReader.ReadContent(page))));
        Check(historicalPdfText.Contains("Operating times") && historicalPdfText.Contains("Daily 09:00 to 18:00"),
            "regenerated historical PDF preserves operatingTimes");

        saved.Details.LicenceSpecificDetailsJson = "{\"activityType\":\"Arcade\"}";
        using var newPdf = PdfReader.Open(new MemoryStream(new ApplicationPdfService().Generate(saved)), PdfDocumentOpenMode.Import);
        var newPdfText = string.Concat(newPdf.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .SelectMany(page => PdfStrings(ContentReader.ReadContent(page))));
        Check(!newPdfText.Contains("Operating times"), "new PDF does not introduce duplicate operating times");

        saved.LicenceType = hawker.Name;
        saved.Details.LicenceSpecificDetailsJson = "{\"goodsSold\":\"Fruit\",\"tradingTimes\":\"Weekdays 08:00 to 16:00\"}";
        using var historicalHawkerPdf = PdfReader.Open(new MemoryStream(new ApplicationPdfService().Generate(saved)), PdfDocumentOpenMode.Import);
        var historicalHawkerPdfText = string.Concat(historicalHawkerPdf.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .SelectMany(page => PdfStrings(ContentReader.ReadContent(page))));
        Check(historicalHawkerPdfText.Contains("Trading days and hours") && historicalHawkerPdfText.Contains("Weekdays 08:00 to 16:00"),
            "regenerated historical PDF preserves tradingTimes");

        saved.Details.LicenceSpecificDetailsJson = "{\"goodsSold\":\"Fruit\"}";
        using var newHawkerPdf = PdfReader.Open(new MemoryStream(new ApplicationPdfService().Generate(saved)), PdfDocumentOpenMode.Import);
        var newHawkerPdfText = string.Concat(newHawkerPdf.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .SelectMany(page => PdfStrings(ContentReader.ReadContent(page))));
        Check(!newHawkerPdfText.Contains("Trading days and hours"), "new PDF does not introduce duplicate trading days and hours");
    }
}
finally
{
    try { File.Delete(database); } catch (IOException) { }
}

Console.WriteLine($"Application entry: {checks} checks passed.");
