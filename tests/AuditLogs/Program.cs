using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Fonts;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using BusinessLicensing_Practice.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PdfSharp.Fonts;

GlobalFontSettings.FontResolver = new LiberationSansFontResolver();
var root = Path.Combine(Path.GetTempPath(), $"audit-logs-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
var database = Path.Combine(root, "audit.db");
var services = new ServiceCollection();
services.AddLogging();
services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={database}"));
services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
services.AddSingleton<IApplicationNotificationService, NoNotifications>();
services.AddSingleton<IWebHostEnvironment>(new TestEnvironment(root));
services.AddSingleton<IPrivateFileStore, LocalPrivateFileStore>();
services.AddSingleton<ApplicationFileService>();
services.AddSingleton<ProtectedUploadService>();
services.AddSingleton<ApplicationPdfService>();
services.AddScoped<MunicipalMessageService>();
services.AddScoped<ApplicationAuditService>();
services.AddSingleton(new ArcGisGeocodingService(new HttpClient(new JsonHandler(
    "{\"candidates\":[{\"address\":\"12 Main Street\",\"attributes\":{\"Addr_type\":\"PointAddress\"},\"location\":{\"x\":18.1,\"y\":-33.1}}]}")),
    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ArcGIS:ApiKey"] = "test" }).Build()));
services.AddSingleton(new WcgMunicipalBoundaryService(new HttpClient(new JsonHandler(
    "{\"features\":[{\"attributes\":{\"AFRIGIS_LocalMunicipalities.S12_NAME\":\"Bergrivier Local Municipality\",\"AFRIGIS_LocalMunicipalities.MUN_CODE\":\"WC013\"}}]}"))));
services.AddSingleton<MunicipalRoutingService>();
services.AddScoped<ApplicationCorrectionService>();

var provider = services.BuildServiceProvider();
var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
await db.Database.MigrateAsync();
var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
foreach (var role in new[] { "BusinessOwner", "MunicipalOfficial", "DEDATAdmin" })
    Ensure((await roles.CreateAsync(new IdentityRole(role))).Succeeded, $"create {role} role");

async Task<ApplicationUser> CreateUser(string name, string role, string? municipality = null)
{
    var email = name.Replace(" ", ".", StringComparison.Ordinal).ToLowerInvariant() + "@example.test";
    var user = new ApplicationUser { UserName = email, Email = email, FullName = name, Municipality = municipality };
    Ensure((await users.CreateAsync(user, "Password123!")).Succeeded, $"create {name}");
    Ensure((await users.AddToRoleAsync(user, role)).Succeeded, $"assign {role} to {name}");
    return user;
}

var owner = await CreateUser("Applicant One", "BusinessOwner");
var official = await CreateUser("Original Official", "MunicipalOfficial", "Bergrivier Municipality");
var otherOfficial = await CreateUser("Other Official", "MunicipalOfficial", "Swartland Municipality");
var admin = await CreateUser("DEDAT Admin", "DEDATAdmin");
ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));

var details = new ApplicationDetails
{
    ApplicationType = "New application", ApplicantFirstName = "Applicant", ApplicantLastName = "One",
    ApplicantAddressLine1 = "1 Home Road", ApplicantSuburb = "Central", ApplicantCity = "Piketberg", ApplicantPostalCode = "7320",
    ApplicantTelephone = "0211234567", ApplicantEmail = "applicant@example.test", PostalAddressSameAsBusiness = true,
    ContactPerson = "Applicant One", BusinessTelephone = "0211234567", BusinessEmail = "business@example.test",
    TradingHours = ApplicationEntry.SerializeTradingHours(ApplicationEntry.Days.Select(day => new TradingDay { Day = day, IsOpen = false }).ToList()),
    OpenOnPublicHolidays = false,
    LicenceSpecificDetailsJson = JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["foodDescription"] = "Meals", ["foodHandling"] = "Pre-packed", ["tradingInformation"] = "Counter service"
    }), DeclarationAccepted = true, DeclarationAcceptedAt = DateTime.UtcNow
};
var application = new Application
{
    ApplicationNumber = "AUD-2026-001", UserId = owner.Id, Municipality = "Bergrivier Municipality", Status = ApplicationWorkflow.Submitted,
    LicenceType = "Sale of Meals Licence", BusinessName = "Audit Cafe", RegistrationNumber = "2026/123456/07", TaxNumber = "1234567890",
    BusinessCategory = "Food", PlaceOfBusinessAddress = "12 Main Street, Piketberg, 7320", PlaceOfBusinessAddressLine1 = "12 Main Street",
    PlaceOfBusinessSuburb = "Central", PlaceOfBusinessCity = "Piketberg", PlaceOfBusinessPostalCode = "7320", PopiaConsentAccepted = true,
    DateSubmitted = DateTime.UtcNow, Details = details,
    Documents = LicenceApplicationCatalog.Find("Sale of Meals Licence")!.Documents.Select(document =>
        new ApplicationDocument { DocumentType = document.DocumentType, FileName = document.DocumentType + ".pdf", FilePath = "/uploads/fixture.pdf" }).ToList()
};
db.Applications.Add(application);
await db.SaveChangesAsync();

var messages = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
await messages.SaveReviewAsync(Principal(official), application.Id, ApplicationWorkflow.UnderReview, "Review started");
var first = await db.ApplicationAuditLogs.OrderBy(item => item.Id).ToListAsync();
Ensure(first.Count == 2 && first[0].EventType == ApplicationAuditEventTypes.StatusChanged &&
    first[0].PreviousStatus == ApplicationWorkflow.Submitted && first[0].NewStatus == ApplicationWorkflow.UnderReview,
    "status change stores old and new status");
Ensure(first[1].EventType == ApplicationAuditEventTypes.ReviewMessageSent && first[1].MetadataJson!.Contains("Review started"),
    "review message creates its own event with message detail");
Ensure(first.All(item => item.ActorUserId == official.Id && item.ActorDisplayName == "Original Official" &&
    item.ActorRole == "MunicipalOfficial" && item.Municipality == "Bergrivier Municipality"), "actor context is snapshotted");

await messages.SaveReviewAsync(Principal(official), application.Id, ApplicationWorkflow.FinalDecision, "Ready for decision");
var beforeApproval = await db.ApplicationAuditLogs.CountAsync();
await messages.SaveReviewAsync(Principal(official), application.Id, ApplicationWorkflow.LicenceIssued, "Approved after assessment");
var approvalEvents = await db.ApplicationAuditLogs.Where(item => item.Id > beforeApproval).ToListAsync();
Ensure(approvalEvents.Count == 1 && approvalEvents[0].EventType == ApplicationAuditEventTypes.ApplicationApproved,
    "approval is one semantic event without status/message duplicates");

db.ChangeTracker.Clear();
await db.Applications.Where(item => item.Id == application.Id).ExecuteUpdateAsync(setters => setters
    .SetProperty(item => item.Status, ApplicationWorkflow.Submitted).SetProperty(item => item.DecisionDateUtc, (DateTime?)null));
await messages.SaveReviewAsync(Principal(official), application.Id, ApplicationWorkflow.Rejected, "Requirements were not met");
var rejection = await db.ApplicationAuditLogs.OrderByDescending(item => item.Id).FirstAsync();
Ensure(rejection.EventType == ApplicationAuditEventTypes.ApplicationRejected && rejection.MetadataJson!.Contains("Requirements were not met"),
    "rejection is one event retaining the decision reason");

await db.Applications.Where(item => item.Id == application.Id).ExecuteUpdateAsync(setters => setters
    .SetProperty(item => item.Status, ApplicationWorkflow.Submitted).SetProperty(item => item.DecisionDateUtc, (DateTime?)null)
    .SetProperty(item => item.DecisionReason, (string?)null));
var corrections = scope.ServiceProvider.GetRequiredService<ApplicationCorrectionService>();
await corrections.RequestCorrectionsAsync(Principal(official), application.Id, "Replace the address document");
var correction = await db.ApplicationAuditLogs.OrderByDescending(item => item.Id).FirstAsync();
Ensure(correction.EventType == ApplicationAuditEventTypes.CorrectionRequested && correction.MetadataJson!.Contains("Replace the address document"),
    "correction request creates one detailed event");

var loaded = await corrections.LoadOrCreateDraftAsync(Principal(owner), application.Id);
var payload = ApplicationDraftService.Deserialize(loaded.Draft.PayloadJson);
payload.DeclarationAccepted = true; payload.PopiaConsentAccepted = true;
await corrections.SaveDraftAsync(Principal(owner), application.Id, payload, 7);
await corrections.ResubmitAsync(Principal(owner), application.Id);
var resubmission = await db.ApplicationAuditLogs.OrderByDescending(item => item.Id).FirstAsync();
Ensure(resubmission.EventType == ApplicationAuditEventTypes.ApplicationResubmitted && resubmission.ActorUserId == owner.Id &&
    resubmission.ActorRole == "BusinessOwner" && resubmission.NewStatus == ApplicationWorkflow.Submitted &&
    resubmission.MetadataJson!.Contains("\"revisionNumber\":2"), "applicant resubmission stores actor and resulting revision");

await db.Users.Where(item => item.Id == official.Id).ExecuteUpdateAsync(setters => setters
    .SetProperty(item => item.FullName, "Renamed Official").SetProperty(item => item.Municipality, "Swartland Municipality"));
db.ChangeTracker.Clear();
var historical = await db.ApplicationAuditLogs.OrderBy(item => item.Id).FirstAsync();
Ensure(historical.ActorDisplayName == "Original Official" && historical.Municipality == "Bergrivier Municipality",
    "historical actor and municipality snapshots survive live user changes");

var auditService = scope.ServiceProvider.GetRequiredService<ApplicationAuditService>();
var result = await auditService.ListAsync(Principal(admin), new("AUD-2026", "Original Official", "Bergrivier Municipality", null, null, null));
Ensure(result.TotalCount > 0 && result.Records.All(item => item.ApplicationNumber == "AUD-2026-001"), "DEDATAdmin can filter audit records");
await Denied(() => auditService.ListAsync(Principal(official), new(null, null, null, null, null, null)));
await Denied(() => auditService.ListAsync(Principal(otherOfficial), new(null, null, null, null, null, null)));
Ensure(true, "Municipal Officials cannot query admin audit logs");
Ensure(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(), "SQLite model and migration are current");

await scope.DisposeAsync();
await provider.DisposeAsync();
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
Directory.Delete(root, true);
Console.WriteLine("All audit log integration checks passed.");

void Ensure(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS: " + description);
}
async Task Denied(Func<Task> action)
{
    try { await action(); } catch (UnauthorizedAccessException) { return; }
    throw new Exception("Expected authorization rejection");
}

sealed class NoNotifications : IApplicationNotificationService
{
    public Task NotifySubmissionAsync(int applicationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task NotifyReviewAsync(int applicationId, string previousStatus, string newStatus, string? municipalMessage, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task NotifyResubmissionAsync(int applicationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
sealed class JsonHandler(string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
}
sealed class TestEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "AuditLogs";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
}
