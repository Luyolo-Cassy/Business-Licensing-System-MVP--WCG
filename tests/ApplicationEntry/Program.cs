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
Check(ApplicationEntry.FormatTradingHours(hours).Contains("Monday: 08:00 – 17:00") &&
    ApplicationEntry.FormatTradingHours(hours).Contains("Sunday: Closed"), "weekly hours format");
Check(ApplicationEntry.FormatTradingHours("8 till late") == "8 till late", "legacy hours fallback");
Check(ApplicationEntry.ValidatePostalAddress(null, "", "", "", "") != null, "postal choice required");
Check(ApplicationEntry.ValidatePostalAddress(false, "", "Central", "Cape Town", "8000") != null, "manual postal fields required");
Check(ApplicationEntry.ValidatePostalAddress(true, "", "", "", "") == null, "same as business needs no duplicate fields");

var standardDocumentNames = new[] { "Certificate of Incorporation", "Proof of Address", "Tax Clearance Certificate", "Owner ID Document" };
var foodDocumentNames = standardDocumentNames.Append("Certificate of Acceptability (CoA)").ToArray();
foreach (var licence in LicenceApplicationCatalog.Licences)
{
    var expected = licence.Id is "sale-of-meals" or "sale-of-perishable-foodstuffs" or "hawker-street-trading"
        ? foodDocumentNames
        : standardDocumentNames;
    Check(licence.Documents.Select(document => document.DocumentType).SequenceEqual(expected), $"{licence.Name} has the expected supporting documents");
    Check(licence.Documents.All(document => document.Required), $"{licence.Name} supporting documents remain required");
}

var details = new ApplicationDetails
{
    ApplicantFirstName = "Ada", ApplicantLastName = "Lovelace", ApplicantAddressLine1 = "1 Main Road",
    ApplicantSuburb = "Gardens", ApplicantCity = "Cape Town", ApplicantPostalCode = "8001",
    PostalAddressSameAsBusiness = true, PostalAddressLine1 = "stale hidden address", TradingHours = hours
};
var application = new Application
{
    ApplicationNumber = "APP-TEST-001", BusinessName = "Example Business", RegistrationNumber = "2024/123456/07",
    TaxNumber = "0123456789", PlaceOfBusinessAddressLine1 = "2 Market Street", PlaceOfBusinessSuburb = "CBD",
    PlaceOfBusinessCity = "Cape Town", PlaceOfBusinessPostalCode = "8000", Details = details
};
application.Documents.Add(new ApplicationDocument { DocumentType = "Certificate of Acceptability Application", FileName = "legacy-coa.pdf", FilePath = "/uploads/legacy-coa.pdf" });
application.Documents.Add(new ApplicationDocument { DocumentType = "Proof of Soundproofing", FileName = "legacy-soundproofing.pdf", FilePath = "/uploads/legacy-soundproofing.pdf" });
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
        Check(ApplicationEntry.FullName(old) == "Legacy Name" && ApplicationEntry.ApplicantAddress(old) == "Legacy Address" && old.TradingHours == "9 to 5", "migration preserves legacy values");
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
        Check(saved.Details?.PostalAddressSameAsBusiness == false && ApplicationEntry.PostalAddress(saved).StartsWith("PO Box 12"), "separate postal persistence");
        Check(ApplicationEntry.FormatTradingHours(saved.Details!.TradingHours).Contains("Sunday: Closed"), "trading hours persistence");
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
        Check(pdfText.Contains("legacy-coa.pdf") && pdfText.Contains("legacy-soundproofing.pdf"), "PDF includes legacy supporting documents");
    }
}
finally
{
    try { File.Delete(database); } catch (IOException) { }
}

Console.WriteLine($"Application entry: {checks} checks passed.");
