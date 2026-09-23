using System.IO.Compression;
using System.Security.Claims;
using System.Xml.Linq;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Fonts;

GlobalFontSettings.UseWindowsFontsUnderWindows = true;
var root = Path.Combine(Path.GetTempPath(), "reporting-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var services = new ServiceCollection(); services.AddLogging(); services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(root, "test.db")}"));
services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
services.AddScoped<ReportingService>(); services.AddScoped<MunicipalMessageService>(); services.AddSingleton<ReportExportService>();
await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.EnsureCreatedAsync();
var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
foreach (var role in new[] { "BusinessOwner", "MunicipalOfficial", "DEDATAdmin" }) await roles.CreateAsync(new IdentityRole(role));
async Task<ApplicationUser> User(string email, string role, string? municipality = null) { var u = new ApplicationUser { UserName = email, Email = email, FullName = email, Municipality = municipality }; Check((await users.CreateAsync(u, "TestOnly!2026#")).Succeeded, "create " + role); Check((await users.AddToRoleAsync(u, role)).Succeeded, "role " + role); return u; }
ClaimsPrincipal Principal(ApplicationUser u) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id), new Claim("AspNet.Identity.SecurityStamp", u.SecurityStamp!)], "test"));
var owner = await User("owner@example.test", "BusinessOwner"); var official = await User("official@example.test", "MunicipalOfficial", "Bergrivier Municipality"); var admin = await User("admin@example.test", "DEDATAdmin");
Application App(string number, string municipality, string status, DateTime submitted, DateTime? decision = null, string licence = "Sale of Meals Licence", string type = "New application") => new() { ApplicationNumber = number, BusinessName = number + " Business", Municipality = municipality, Status = status, DateSubmitted = submitted, DecisionDateUtc = decision, LicenceType = licence, UserId = owner.Id, Details = new ApplicationDetails { ApplicationType = type } };
db.Applications.AddRange(
    App("A", "Bergrivier Municipality", "Licence Issued", new(2026, 9, 5), new(2026, 9, 20)),
    App("B", "Bergrivier Municipality", "Rejected", new(2026, 8, 20), new(2026, 9, 10), "Health Facility Licence"),
    App("C", "Bergrivier Municipality", "Under Review", new(2026, 9, 5)),
    App("D", "Bergrivier Municipality", "Withdrawn", new(2026, 9, 5)),
    App("E", "Bergrivier Municipality", "Licence Issued", new(2026, 7, 1)),
    App("OTHER", "Swartland Municipality", "Under Review", new(2026, 9, 3)));
await db.SaveChangesAsync();
var reporting = scope.ServiceProvider.GetRequiredService<ReportingService>(); var exports = scope.ServiceProvider.GetRequiredService<ReportExportService>();

var status = await reporting.GenerateAsync(Principal(official), ReportAudience.MunicipalOfficial, ReportKeys.Status, new());
Check(status.Summaries.Single().Value == "5" && status.Rows.All(r => int.Parse(r.Values["Applications"]) > 0), "status report is scoped and hides zero categories");
var september = new ReportFilter { From = new(2026, 9, 1), To = new(2026, 9, 30) };
var processing = await reporting.GenerateAsync(Principal(official), ReportAudience.MunicipalOfficial, ReportKeys.Processing, september);
var sep = processing.Rows.Single(r => r.Values["Period"] == "2026 Sep");
Check(sep.Values["Applications Received"] == "3" && sep.Values["Decisions Made"] == "2", "processing scenarios A-D use submission and decision event dates; withdrawal is not a decision");
var augustSeptember = await reporting.GenerateAsync(Principal(official), ReportAudience.MunicipalOfficial, ReportKeys.Processing, new() { From = new(2026, 8, 1), To = new(2026, 9, 30) });
var aug = augustSeptember.Rows.Single(r => r.Values["Period"] == "2026 Aug");
Check(aug.Values["Applications Received"] == "1" && aug.Values["Decisions Made"] == "0", "August submission and September decision are attributed to different periods");
var quarters = await reporting.GenerateAsync(Principal(official), ReportAudience.MunicipalOfficial, ReportKeys.Processing, new() { From = new(2026, 7, 1), To = new(2026, 9, 30), Grouping = "Quarterly" });
Check(quarters.Rows.Single().Values["Period"] == "2026 Q3", "quarterly event grouping");

var time = await reporting.GenerateAsync(Principal(official), ReportAudience.MunicipalOfficial, ReportKeys.ProcessingTime, september);
Check(time.Rows.Count == 2 && time.Rows.Any(r => r.Values["Outcome"] == "Licence Issued" && r.Values["Processing Days"] == "15.0") && time.Rows.Any(r => r.Values["Outcome"] == "Rejected" && r.Values["Processing Days"] == "21.0"), "processing time is decision-date filtered and includes issued and rejected durations");
Check(time.Rows.All(r => r.Values["Application Number"] != "D" && r.Values["Application Number"] != "E"), "withdrawals and missing decision dates are excluded from processing time");

var municipality = await reporting.GenerateAsync(Principal(admin), ReportAudience.DedatAdmin, ReportKeys.MunicipalityProcessing, september);
var berg = municipality.Rows.Single(r => r.Values["Municipality"] == "Bergrivier Municipality"); var swart = municipality.Rows.Single(r => r.Values["Municipality"] == "Swartland Municipality");
Check(berg.Values["Decisions Made"] == "2" && berg.Values["Decisions Measured"] == "2" && berg.Values["Average Processing Days"] == "18.0", "municipality decision count, sample size and average use decision dates");
Check(swart.Values["Currently Pending"] == "1" && swart.Values["Decisions Made"] == "0" && municipality.Chart.Labels.All(x => x != "Swartland Municipality"), "current pending is retained and missing average is not graphed as zero");

var forged = await reporting.GenerateAsync(Principal(official), ReportAudience.MunicipalOfficial, ReportKeys.Status, new() { Municipality = "Swartland Municipality" });
Check(forged.Scope == "Bergrivier Municipality" && forged.Summaries.Single().Value == "5", "forged official municipality is ignored");
var forgedPending = await reporting.GenerateAsync(Principal(official), ReportAudience.MunicipalOfficial, ReportKeys.Pending, new() { Municipality = "Swartland Municipality" });
var adminAll = await reporting.GenerateAsync(Principal(admin), ReportAudience.DedatAdmin, ReportKeys.Status, new()); Check(adminAll.Summaries.Single().Value == "6", "admin province-wide access");
var adminFiltered = await reporting.GenerateAsync(Principal(admin), ReportAudience.DedatAdmin, ReportKeys.Status, new() { Municipality = "Swartland Municipality" }); Check(adminFiltered.Summaries.Single().Value == "1", "admin municipality filter");
official.Municipality = "Swartland Municipality"; Check((await users.UpdateAsync(official)).Succeeded, "reassign official");
var reassigned = await reporting.GenerateAsync(Principal(official), ReportAudience.MunicipalOfficial, ReportKeys.Status, new() { Municipality = "Bergrivier Municipality" }); Check(reassigned.Scope == "Swartland Municipality" && reassigned.Summaries.Single().Value == "1", "reassigned official receives current scope");
await Reject(() => reporting.GenerateAsync(Principal(owner), ReportAudience.MunicipalOfficial, ReportKeys.Status, new()), "owner denied"); await Reject(() => reporting.GenerateAsync(new ClaimsPrincipal(), ReportAudience.DedatAdmin, ReportKeys.Status, new()), "anonymous denied");

var workbook = exports.Excel(forged); Check(workbook[0] == 'P' && workbook[1] == 'K', "Excel export is a genuine zipped .xlsx workbook");
using (var archive = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read))
{
    Check(archive.GetEntry("xl/workbook.xml") != null && archive.GetEntry("xl/worksheets/sheet1.xml") != null, "Excel workbook contains Open XML workbook and worksheet parts");
    using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open()); var sheet = XDocument.Parse(reader.ReadToEnd()); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var rows = sheet.Descendants(ns + "row").ToList(); var headings = rows.Single(r => (string?)r.Attribute("r") == "4").Elements(ns + "c").ToList();
    Check(headings.Count == 2 && headings[0].Value == "Status" && headings[1].Value == "Applications", "Excel report columns occupy separate cells");
    var numericApplicationCells = rows.Where(r => (int?)r.Attribute("r") >= 5).SelectMany(r => r.Elements(ns + "c")).Where(c => ((string?)c.Attribute("r"))?.StartsWith('B') == true).ToList();
    Check(numericApplicationCells.Count > 0 && numericApplicationCells.All(c => c.Attribute("t") == null && c.Element(ns + "v") != null), "Excel application counts are numeric cells");
}
var scopedWorkbook = exports.Excel(forgedPending); using (var scopedArchive = new ZipArchive(new MemoryStream(scopedWorkbook), ZipArchiveMode.Read)) { using var reader = new StreamReader(scopedArchive.GetEntry("xl/worksheets/sheet1.xml")!.Open()); var xml = reader.ReadToEnd(); Check(xml.Contains("C Business") && !xml.Contains("OTHER Business"), "municipal Excel contains only assigned-municipality application rows despite forged filter"); }
var filteredWorkbook = exports.Excel(adminFiltered); Check(filteredWorkbook.Length > 0, "admin filtered Excel export succeeds");
var pdf = exports.Pdf(processing, september, DateTime.UtcNow); Check(pdf.Length > 4 && pdf[0] == '%' && pdf[1] == 'P', "PDF uses corrected report dataset");
Console.WriteLine("All reporting calculation, authorization and export checks passed.");
try { Directory.Delete(root, true); } catch { }

void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); }
async Task Reject(Func<Task<ReportResult>> action, string message) { try { await action(); } catch (UnauthorizedAccessException) { Console.WriteLine("PASS: " + message); return; } throw new Exception(message); }
