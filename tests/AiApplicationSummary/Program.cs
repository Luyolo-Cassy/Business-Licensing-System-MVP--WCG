using System.Net;
using System.Security.Claims;
using System.Text;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var database = Path.Combine(Path.GetTempPath(), $"ai-summary-{Guid.NewGuid():N}.db");
var fake = new FakeProvider();
var services = new ServiceCollection(); services.AddLogging();
services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={database}"));
services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
services.AddSingleton<IAiApplicationSummaryProvider>(fake); services.AddScoped<ApplicationSummaryService>(); services.AddScoped<AiSettingsService>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.MigrateAsync();
var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
foreach (var role in new[] { "DEDATAdmin", "MunicipalOfficial", "BusinessOwner" }) Check((await roles.CreateAsync(new(role))).Succeeded, "create " + role);
async Task<ApplicationUser> User(string name, string role, string? municipality = null)
{
    var user = new ApplicationUser { UserName = name + "@example.test", Email = name + "@example.test", FullName = name, Municipality = municipality };
    Check((await users.CreateAsync(user)).Succeeded && (await users.AddToRoleAsync(user, role)).Succeeded, "create " + name); return user;
}
var admin = await User("admin", "DEDATAdmin"); var official = await User("official", "MunicipalOfficial", "Hessequa Municipality");
var wrong = await User("wrong", "MunicipalOfficial", "Swartland Municipality"); var owner = await User("owner", "BusinessOwner");
ClaimsPrincipal Principal(ApplicationUser user, string? stamp = null) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim("AspNet.Identity.SecurityStamp", stamp ?? user.SecurityStamp!)], "test"));
var application = new Application
{
    ApplicationNumber = "SUM-001", UserId = owner.Id, Municipality = "Hessequa Municipality", Status = "Under Review", RevisionNumber = 1,
    LicenceType = "Sale of Meals Licence", BusinessName = "Safe Cafe", TradingName = "Cafe", BusinessCategory = "Meals",
    RegistrationNumber = "SECRET-REG", TaxNumber = "SECRET-TAX", PlaceOfBusinessAddressLine1 = "99 Secret Street", PlaceOfBusinessSuburb = "Riversdale", PlaceOfBusinessCity = "Riversdale",
    ApplicationFormFilePath = "/secret/form.pdf", DateSubmitted = DateTime.UtcNow,
    Details = new ApplicationDetails { ApplicationType = "New application", ApplicantEmail = "private@example.test", ApplicantTelephone = "0210000000", BusinessEmail = "secret@example.test", BusinessTelephone = "0220000000", TradingHours = "[]", OpenOnPublicHolidays = false, LicenceSpecificDetailsJson = "{\"foodDescription\":\"Prepared meals\",\"foodHandling\":\"Pre-packed\",\"tradingInformation\":\"Counter service\"}" },
    Documents = [new() { DocumentType = "Owner ID Document", FileName = "secret-id.pdf", FilePath = "/uploads/secret.pdf" }]
};
db.Applications.Add(application); await db.SaveChangesAsync();
var summaries = scope.ServiceProvider.GetRequiredService<ApplicationSummaryService>(); var settings = scope.ServiceProvider.GetRequiredService<AiSettingsService>();
Check((await db.AiSettings.SingleAsync()).ApplicationSummariesEnabled == false, "summaries default OFF");
var disabled = await summaries.GenerateAsync(Principal(official), application.Id); Check(disabled.Status == AiApplicationSummaryStatus.Disabled && fake.Calls == 0, "disabled makes zero calls");
await settings.SetApplicationSummariesEnabledAsync(Principal(admin), true); Check(await settings.GetApplicationSummariesEnabledAsync(Principal(admin)), "admin enables summaries");
Check(!await settings.GetDocumentValidationEnabledAsync(Principal(admin)), "document validation remains independently OFF");
await settings.SetDocumentValidationEnabledAsync(Principal(admin), true); Check(await settings.GetDocumentValidationEnabledAsync(Principal(admin)) && await settings.GetApplicationSummariesEnabledAsync(Principal(admin)), "both settings can be ON");
await settings.SetApplicationSummariesEnabledAsync(Principal(admin), false); Check(await settings.GetDocumentValidationEnabledAsync(Principal(admin)) && !await settings.GetApplicationSummariesEnabledAsync(Principal(admin)), "document ON summary OFF combination");
await settings.SetDocumentValidationEnabledAsync(Principal(admin), false); await settings.SetApplicationSummariesEnabledAsync(Principal(admin), true); Check(!await settings.GetDocumentValidationEnabledAsync(Principal(admin)), "document OFF summary ON combination");
await using (var reloadScope = provider.CreateAsyncScope())
{
    var reloadDb = reloadScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Check((await reloadDb.AiSettings.AsNoTracking().SingleAsync()).ApplicationSummariesEnabled, "summary setting persists across DbContext reload");
}
foreach (var deniedUser in new[] { owner, official }) await Denied(() => settings.SetApplicationSummariesEnabledAsync(Principal(deniedUser), false));
foreach (var deniedPrincipal in new[] { Principal(wrong), Principal(owner), new ClaimsPrincipal() }) await Denied(() => summaries.GenerateAsync(deniedPrincipal, application.Id));
Check(fake.Calls == 0, "unauthorized and wrong municipality make zero calls");
fake.Result = new(true, "A concise factual summary."); var generated = await summaries.GenerateAsync(Principal(official), application.Id);
Check(generated.Status == AiApplicationSummaryStatus.Generated && fake.Calls == 1, "authorized uncached generation calls provider once");
var input = fake.Input!; Check(input.ApplicationNumber == "SUM-001" && input.RevisionNumber == 1 && input.LicenceSpecificInformation.Any(x => x.Question == "Description of food or meals sold") && input.SupportingDocumentTypes.SequenceEqual(["Owner ID Document"]), "safe structured input includes required data");
var serialized = System.Text.Json.JsonSerializer.Serialize(input); foreach (var forbidden in new[] { "SECRET-REG", "SECRET-TAX", "private@example", "0210000000", "secret-id.pdf", "/uploads/", "/secret/form.pdf", "99 Secret Street", official.Id }) Check(!serialized.Contains(forbidden, StringComparison.Ordinal), "input excludes " + forbidden);
var cached = await summaries.GenerateAsync(Principal(official), application.Id); Check(cached.Status == AiApplicationSummaryStatus.Cached && fake.Calls == 1, "cached generation makes zero additional calls");
Check((await summaries.GetCurrentAsync(Principal(official), application.Id)).Status == AiApplicationSummaryStatus.Cached, "page lookup returns cache without provider");
Check(await db.ApplicationAuditLogs.CountAsync(x => x.EventType == ApplicationAuditEventTypes.AiSummaryGenerated) == 1, "new generation creates one audit event");
var audit = await db.ApplicationAuditLogs.SingleAsync(x => x.EventType == ApplicationAuditEventTypes.AiSummaryGenerated); Check(!audit.MetadataJson!.Contains("concise factual", StringComparison.OrdinalIgnoreCase), "audit excludes summary text");
await db.Applications.Where(x => x.Id == application.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevisionNumber, 2));
Check((await summaries.GetCurrentAsync(Principal(official), application.Id)).Status == AiApplicationSummaryStatus.Available, "revision 1 cache is not current for revision 2");
fake.Result = new(false); var unavailable = await summaries.GenerateAsync(Principal(official), application.Id); Check(unavailable.Status == AiApplicationSummaryStatus.Unavailable && await db.ApplicationAiSummaries.CountAsync() == 1 && await db.ApplicationAuditLogs.CountAsync(x => x.EventType == ApplicationAuditEventTypes.AiSummaryGenerated) == 1, "failure stores no summary or audit and does not change workflow");
Check((await db.Applications.AsNoTracking().SingleAsync(x => x.Id == application.Id)).Status == "Under Review", "failure does not alter review status");
fake.Result = new(true, "Revision two summary."); await summaries.GenerateAsync(Principal(official), application.Id); Check(await db.ApplicationAiSummaries.CountAsync() == 2, "revision 2 stores separate summary");
var oldStamp = official.SecurityStamp!; await users.UpdateSecurityStampAsync(official); await Denied(() => summaries.GenerateAsync(Principal(official, oldStamp), application.Id));
Check(fake.Calls == 3, "stale security official makes zero calls");
await users.SetLockoutEnabledAsync(official, true); await users.SetLockoutEndDateAsync(official, DateTimeOffset.UtcNow.AddHours(1));
await Denied(() => summaries.GenerateAsync(Principal(official), application.Id)); Check(fake.Calls == 3, "locked official makes zero calls");
await users.SetLockoutEndDateAsync(official, DateTimeOffset.UtcNow.AddMinutes(-1)); official.Municipality = "Swartland Municipality"; Check((await users.UpdateAsync(official)).Succeeded, "reassign official municipality");
await Denied(() => summaries.GenerateAsync(Principal(official), application.Id)); Check(fake.Calls == 3, "reassigned official makes zero calls");
official.Municipality = "Hessequa Municipality"; Check((await users.UpdateAsync(official)).Succeeded && (await users.RemoveFromRoleAsync(official, "MunicipalOfficial")).Succeeded, "deactivate official role");
await Denied(() => summaries.GenerateAsync(Principal(official), application.Id)); Check(fake.Calls == 3, "deactivated official makes zero calls");

var noKeyHandler = new CountingHandler(HttpStatusCode.OK, "{}"); var noKey = new GeminiApplicationSummaryService(new HttpClient(noKeyHandler), new ConfigurationBuilder().Build());
Check(!(await noKey.GenerateAsync(input)).Success && noKeyHandler.Calls == 0, "missing API key makes zero HTTP calls");
foreach (var handler in new HttpMessageHandler[] { new CountingHandler(HttpStatusCode.TooManyRequests, "{}"), new CountingHandler(HttpStatusCode.OK, "not-json"), new CountingHandler(HttpStatusCode.OK, "{\"candidates\":[]}"), new TimeoutHandler() })
{
    var gemini = new GeminiApplicationSummaryService(new HttpClient(handler), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Gemini:ApiKey"] = "test" }).Build());
    Check(!(await gemini.GenerateAsync(input)).Success, "provider failure returns unavailable");
}
Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(), "SQLite model is current");
Console.WriteLine("All AI application summary checks passed. No real Gemini requests were made.");
void Check(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS: " + text); }
async Task Denied(Func<Task> action) { try { await action(); } catch (UnauthorizedAccessException) { return; } throw new Exception("Expected denial"); }
sealed class FakeProvider : IAiApplicationSummaryProvider { public int Calls; public AiApplicationSummaryInput? Input; public AiSummaryProviderResult Result = new(false); public Task<AiSummaryProviderResult> GenerateAsync(AiApplicationSummaryInput input, CancellationToken cancellationToken = default) { Calls++; Input = input; return Task.FromResult(Result); } }
sealed class CountingHandler(HttpStatusCode status, string body) : HttpMessageHandler { public int Calls; protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }); } }
sealed class TimeoutHandler : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(new TaskCanceledException()); }
