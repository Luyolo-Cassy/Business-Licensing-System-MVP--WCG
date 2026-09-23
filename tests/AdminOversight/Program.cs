using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.RegularExpressions;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System.Text.Json;

// Run from the repository root. All test users, history and PDFs live in a temporary content root.
var root = Path.Combine(Path.GetTempPath(), "admin-oversight-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var appDll = Path.GetFullPath("bin/Debug/net10.0/ProvincialBusinessLicensingSystem.dll");
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var baseUrl = $"http://127.0.0.1:{port}";
Process? host = null;
var logs = new List<string>();
void Check(bool condition, string text)
{
    if (!condition) throw new Exception(text);
    Console.WriteLine("PASS: " + text);
}
async Task Start()
{
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in new[] { appDll, "--contentRoot", root, "--urls", baseUrl, "--environment", "Development",
        "--Logging:LogLevel:Default", "Warning", "--Logging:LogLevel:Microsoft.AspNetCore", "Warning",
        "--Logging:EventLog:LogLevel:Default", "None" }) start.ArgumentList.Add(arg);
    host = Process.Start(start)!;
    host.OutputDataReceived += (_, e) => { if (e.Data != null) lock (logs) logs.Add(e.Data); };
    host.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (logs) logs.Add(e.Data); };
    host.BeginOutputReadLine(); host.BeginErrorReadLine();
    using var client = Client();
    for (var attempt = 0; attempt < 80; attempt++)
    {
        if (host.HasExited) throw new Exception("Host exited during startup");
        try { if ((await client.GetAsync("/Account/Login")).StatusCode == HttpStatusCode.OK) return; } catch (HttpRequestException) { }
        await Task.Delay(250);
    }
    throw new Exception("Host failed to start");
}
void Stop() { if (host is { HasExited: false }) { host.Kill(); host.WaitForExit(); } host?.Dispose(); host = null; }
HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, CookieContainer = new() }) { BaseAddress = new(baseUrl) };
async Task<HttpResponseMessage> Form(HttpClient client, string url, Dictionary<string, string> fields)
{
    var html = await client.GetStringAsync(url);
    fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value);
    return await client.PostAsync(url, new FormUrlEncodedContent(fields));
}
async Task<HttpClient> Login(string email, string password, string destination)
{
    var client = Client();
    var response = await Form(client, "/Account/Login", new() { ["_handler"] = "login", ["Input.Email"] = email, ["Input.Password"] = password });
    Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.ToString().EndsWith(destination, StringComparison.OrdinalIgnoreCase), "Normal login redirect: " + destination);
    return client;
}
async Task Reject<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception("Expected rejection: " + typeof(T).Name);
}
ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity([
    new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!)], "test"));

try
{
    await Start();
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, ApplicationName = typeof(ApplicationUser).Assembly.GetName().Name });
    builder.Logging.ClearProviders();
    builder.Services.AddDataProtection();
    builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(root, "businesslicensing.db")}"));
    builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
    builder.Services.AddScoped<AdminApplicationService>();
    builder.Services.AddScoped<OfficialManagementService>();
    builder.Services.AddScoped<MunicipalMessageService>();
    await using var provider = builder.Services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var oversight = scope.ServiceProvider.GetRequiredService<AdminApplicationService>();
    var management = scope.ServiceProvider.GetRequiredService<OfficialManagementService>();
    var messages = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
    var admin = Principal((await users.FindByEmailAsync("dedat.admin@example.test"))!);
    Check((await users.GetUsersInRoleAsync("MunicipalOfficial")).Count == 0, "Startup still provisions zero Officials");
    var empty = await oversight.ReportAsync(admin);
    Check(empty.Total == 0 && empty.Municipalities.Count == 5 && empty.Municipalities.All(m => m.Count == 0), "Empty reports show five municipalities with zero counts");
    async Task<ApplicationUser> Owner(string name)
    {
        var user = new ApplicationUser { UserName = name + "@example.test", Email = name + "@example.test", FullName = name + " Identity", PhoneNumber = "0210000000" };
        Check((await users.CreateAsync(user, "OwnerOnly!2026#")).Succeeded, "Create isolated applicant");
        Check((await users.AddToRoleAsync(user, "BusinessOwner")).Succeeded, "Applicant role assigned");
        return user;
    }
    var owner = await Owner("Alpha"); var otherOwner = await Owner("Beta");
    var invitation = (await management.SaveAsync(admin, null, "Test Official", "oversight.official@example.test", 1))!;
    await management.CompleteSetupAsync(invitation.UserId, invitation.Code, "OfficialOwn!2026#");
    var official = (await users.FindByIdAsync(invitation.UserId))!;
    await db.Municipalities.Where(m => m.Id == 4).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsActive, false));
    Application Fixture(string reference, ApplicationUser applicant, string? municipality, string status, string licence, string? type) => new()
    {
        ApplicationNumber = reference, UserId = applicant.Id, Municipality = municipality, Status = status,
        BusinessName = reference + " Business", LicenceType = licence, PlaceOfBusinessAddress = "1 Main Road, Test Town",
        RegistrationNumber = "REG-TEST", TaxNumber = "TAX-TEST", DateSubmitted = new DateTime(2026, 9, 1, 10, 30, 0),
        Details = type == null ? null : new ApplicationDetails { ApplicationType = type, ApplicantName = applicant.FullName + " Submitted", ApplicantEmail = applicant.Email,
            ApplicantTelephone = "0211234567", LicenceSpecificDetailsJson = "{\"foodDescription\":\"Fresh meals\"}", DeclarationAccepted = true },
        ApplicationFormFilePath = "1/test.pdf", ApplicationFormFileName = "test.pdf"
    };
    var a = Fixture("OV-A", owner, "Bergrivier Municipality", "Submitted", "Sale of Meals Licence", "New");
    var b = Fixture("OV-B", owner, "Bergrivier Municipality", "Licence Issued", "Sale of Meals Licence", "Renewal");
    b.DateSubmitted = new DateTime(2026, 9, 2, 11, 0, 0);
    var c = Fixture("OV-C", otherOwner, "Swartland Municipality", "Rejected", "Health Facility Licence", "New");
    var d = Fixture("OV-D", otherOwner, "Swartland Municipality", "Under Review", "Health Facility Licence", "New");
    var e = Fixture("OV-E", owner, "City of Cape Town", "Withdrawn", "Gaming / Amusement Licence", null);
    var f = Fixture("OV-F", owner, null, "Submitted", "", null);
    a.Documents.Add(new ApplicationDocument { DocumentType = "Proof of Address", FileName = "proof.pdf", FilePath = "/uploads/proof.pdf" });
    db.Applications.AddRange(a, b, c, d, e, f); await db.SaveChangesAsync();
    db.MunicipalMessages.Add(new MunicipalMessage { ApplicationId = a.Id, SenderId = official.Id, SenderName = official.FullName,
        Content = "History <script>alert('test')</script>", CreatedAtUtc = DateTime.UtcNow }); await db.SaveChangesAsync();
    var pdfDir = Path.Combine(root, "App_Data", "generated-applications", "1"); Directory.CreateDirectory(pdfDir);
    var pdf = "%PDF-1.4 oversight test"u8.ToArray(); await File.WriteAllBytesAsync(Path.Combine(pdfDir, "test.pdf"), pdf);
    async Task<string> Snapshot() => JsonSerializer.Serialize(new
    {
        Applications = await db.Applications.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Details = await db.ApplicationDetails.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ApplicationId, x.ApplicantName, x.LicenceSpecificDetailsJson }).ToListAsync(),
        Documents = await db.ApplicationDocuments.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Messages = await db.MunicipalMessages.AsNoTracking().OrderBy(x => x.Id).ToListAsync()
    });
    var before = await Snapshot();
    Check((await oversight.ListAsync(admin)).Applications.Count == 6, "Admin query includes all municipalities and unassigned history");
    Check((await oversight.ListAsync(admin, municipality: "Swartland Municipality")).Applications.Count == 2, "Municipality filter includes inactive history");
    Check((await oversight.ListAsync(admin, status: "Submitted")).Applications.Count == 2, "Status filter");
    Check((await oversight.ListAsync(admin, search: " ov-c ")).Applications.Single().Id == c.Id, "Reference search trims and ignores case");
    Check((await oversight.ListAsync(admin, search: "alpha")).Applications.Count == 4, "Applicant search includes submitted name and legacy Identity-name fallback");
    Check((await oversight.ListAsync(admin, search: "beta", municipality: "Swartland Municipality", status: "Rejected")).Applications.Single().Id == c.Id, "Combined filters");
    Check((await oversight.ListAsync(admin, search: "no match")).Applications.Count == 0, "No-match filter");
    Check((await oversight.ListAsync(admin, reference: "v-b")).Applications.Single().Id == b.Id, "Reference column filter is partial and case-insensitive");
    Check((await oversight.ListAsync(admin, applicant: "beta")).Applications.Count == 2, "Applicant column filter");
    Check((await oversight.ListAsync(admin, licenceType: "Health Facility Licence", applicationType: "New")).Applications.Count == 2, "Licence and application type column filters combine");
    Check((await oversight.ListAsync(admin, columnMunicipality: "Bergrivier Municipality")).Applications.Count == 2, "Municipality column filter");
    Check((await oversight.ListAsync(admin, submitted: new DateTime(2026, 9, 2))).Applications.Single().Id == b.Id, "Submitted-date column filter uses the complete selected day");
    Check((await oversight.ListAsync(admin, columnStatus: "Under Review")).Applications.Single().Id == d.Id, "Status column filter");
    Check((await oversight.ListAsync(admin, applicant: "beta", licenceType: "Health Facility Licence", columnMunicipality: "Swartland Municipality", columnStatus: "Rejected")).Applications.Single().Id == c.Id, "Multiple column filters combine");
    Check((await oversight.ListAsync(admin, search: "alpha", municipality: "Bergrivier Municipality", status: "Licence Issued", reference: "OV-B", applicationType: "Renewal", submitted: new DateTime(2026, 9, 2), columnStatus: "Licence Issued")).Applications.Single().Id == b.Id, "Top and column filters combine with AND semantics");
    var report = await oversight.ReportAsync(admin);
    Check(report.Total == 6 && report.Statuses.Single(x => x.Name == "Submitted").Count == 2 && report.Statuses.Sum(x => x.Count) == 6, "Report total and status counts come from database");
    Check(report.Municipalities.Single(x => x.Name == "Bergrivier Municipality").Count == 2 &&
        report.Municipalities.Single(x => x.Name == "Swartland Municipality") is { Count: 2, IsActive: false } &&
        report.Municipalities.Single(x => x.Name == "Hessequa Municipality").Count == 0 &&
        report.Municipalities.Single(x => x.Name == "City of Cape Town").Count == 1 &&
        report.Municipalities.Single(x => x.Name == "Not assigned").Count == 1, "Municipality reports retain inactive, zero-count, legacy and unassigned records");
    Check(report.LicenceTypes.Single(x => x.Name == "Sale of Meals Licence").Count == 2 && report.LicenceTypes.Sum(x => x.Count) == 6 &&
        report.ApplicationTypes.Single(x => x.Name == "New").Count == 3 && report.ApplicationTypes.Single(x => x.Name == "Not recorded").Count == 2, "Licence and application-type grouping");
    var detail = (await oversight.DetailAsync(admin, a.Id))!;
    Check(detail.Application.User == null && detail.Application.Documents.Count == 1 && detail.Communications.Count == 1, "Detail includes stored documents and messages without loading Identity secrets");
    Check(await oversight.DetailAsync(admin, int.MaxValue) == null, "Missing detail handled");
    foreach (var denied in new[] { Principal(owner), Principal(official), new ClaimsPrincipal() })
    {
        await Reject<UnauthorizedAccessException>(() => oversight.ListAsync(denied));
        await Reject<UnauthorizedAccessException>(() => oversight.DetailAsync(denied, a.Id));
        await Reject<UnauthorizedAccessException>(() => oversight.ReportAsync(denied));
    }
    foreach (var status in new[] { "Licence Issued", "Rejected", "Under Review" })
        await Reject<UnauthorizedAccessException>(() => messages.SaveReviewAsync(admin, a.Id, status, "Admin must not process"));
    using var adminClient = await Login("dedat.admin@example.test", "DevOnly!DEDAT2026#", "/admin-dashboard");
    using var ownerClient = await Login(owner.Email!, "OwnerOnly!2026#", "/dashboard");
    using var officialClient = await Login(official.Email!, "OfficialOwn!2026#", "/official-dashboard");
    var adminExcel = await adminClient.GetAsync("/reports/export?audience=DedatAdmin&report=status&format=xlsx&municipality=Swartland%20Municipality");
    Check(adminExcel.StatusCode == HttpStatusCode.OK && adminExcel.Content.Headers.ContentType?.MediaType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Admin filtered Excel export succeeds");
    var adminPdf = await adminClient.GetAsync("/reports/export?audience=DedatAdmin&report=outcomes&format=pdf");
    Check(adminPdf.StatusCode == HttpStatusCode.OK && adminPdf.Content.Headers.ContentType?.MediaType == "application/pdf", "Admin PDF export succeeds");
    var officialExcel = await officialClient.GetAsync("/reports/export?audience=MunicipalOfficial&report=pending&format=xlsx&municipality=Swartland%20Municipality");
    Check(officialExcel.StatusCode == HttpStatusCode.OK && (await officialExcel.Content.ReadAsByteArrayAsync()) is var bytes && bytes.Length > 2 && bytes[0] == 'P' && bytes[1] == 'K', "Official Excel ignores manipulated municipality and returns a workbook");
    Check((await ownerClient.GetAsync("/reports/export?audience=MunicipalOfficial&report=status&format=xlsx")).StatusCode != HttpStatusCode.OK,
        "Business Owner report export denied");
    Check((await Client().GetAsync("/reports/export?audience=DedatAdmin&report=status&format=xlsx")).StatusCode != HttpStatusCode.OK,
        "Anonymous report export denied");
    var listHtml = await adminClient.GetStringAsync("/admin/applications");
    Check(new[] { "OV-A", "OV-B", "OV-C", "OV-D", "OV-E", "OV-F" }.All(listHtml.Contains), "Admin HTTP list shows all six records");
    var filterUrl = QueryHelpers.AddQueryString("/admin/applications", new Dictionary<string, string?> { ["municipality"] = "Swartland Municipality", ["status"] = "Rejected", ["search"] = "beta" });
    var filteredHtml = await adminClient.GetStringAsync(filterUrl);
    Check(filteredHtml.Contains("OV-C") && !filteredHtml.Contains("OV-A") && !filteredHtml.Contains("OV-D"), "HTTP query-string filters work together");
    var columnUrl = QueryHelpers.AddQueryString("/admin/applications", new Dictionary<string, string?> { ["search"] = "beta", ["columnMunicipality"] = "Swartland Municipality", ["columnStatus"] = "Rejected", ["reference"] = "OV-C" });
    var columnHtml = await adminClient.GetStringAsync(columnUrl);
    Check(columnHtml.Contains("OV-C") && !columnHtml.Contains("OV-D") && columnHtml.Contains("Filter applied") &&
        columnHtml.Contains("Licence / Application Type") && !Regex.IsMatch(columnHtml, @"<th>\s*View\s*<details", RegexOptions.IgnoreCase), "Column-filter UI combines filters, indicates active state and leaves View action-only");
    var detailHtml = await adminClient.GetStringAsync($"/admin/applications/{a.Id}");
    Check(detailHtml.Contains("REG-TEST") && detailHtml.Contains("Fresh meals") && detailHtml.Contains("proof.pdf") && detailHtml.Contains("History &lt;script&gt;"), "Admin HTTP detail renders application, licence, document and escaped communication data");
    Check(!Regex.IsMatch(detailHtml, @"<button\b[^>]*>\s*(Approve|Reject|Save Review|Send)", RegexOptions.IgnoreCase) &&
        !detailHtml.Contains("PasswordHash") && !detailHtml.Contains(owner.SecurityStamp!) && !detailHtml.Contains(owner.PasswordHash!), "Detail exposes no processing buttons or Identity secrets");
    Check((await adminClient.GetAsync($"/review-application/{a.Id}")).StatusCode != HttpStatusCode.OK, "Admin cannot enter Official processing page");
    Check((await adminClient.PostAsync($"/admin/applications/{a.Id}", new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "Licence Issued" }))).StatusCode != HttpStatusCode.OK, "Admin detail has no mutation POST handler");
    Check((await adminClient.GetByteArrayAsync($"/applications/{c.Id}/official-pdf")).SequenceEqual(pdf), "Admin can download PDF from another municipality through existing endpoint");
    var reportHtml = await adminClient.GetStringAsync("/admin/reports");
    Check(reportHtml.Contains("Provincial Licensing Overview") && reportHtml.Contains("Municipality Application Overview"), "Admin HTTP reports show the six-report dashboard");
    foreach (var client in new[] { ownerClient, officialClient })
        foreach (var url in new[] { "/admin/applications", "/admin/reports", $"/admin/applications/{a.Id}" })
        {
            var response = await client.GetAsync(url);
            Check(response.StatusCode == HttpStatusCode.Forbidden || response.Headers.Location?.ToString().Contains("AccessDenied") == true, "Non-admin denied " + url);
        }
    var officialDashboard = await officialClient.GetStringAsync("/official-dashboard");
    Check(officialDashboard.Contains("OV-A") && officialDashboard.Contains("OV-B") && !officialDashboard.Contains("OV-C"), "Official dashboard remains municipality-scoped");
    var officialReport = await officialClient.GetStringAsync("/generate-report");
    Check(officialReport.Contains("Application Status Report") && officialReport.Contains("Processing Time Report"), "Official HTTP reports show the six-report dashboard");
    Check((await officialClient.GetAsync($"/applications/{c.Id}/official-pdf")).StatusCode != HttpStatusCode.OK &&
        (await ownerClient.GetAsync($"/applications/{c.Id}/official-pdf")).StatusCode != HttpStatusCode.OK, "Official and applicant cross-scope PDF access remains denied");
    Check(!(await officialClient.GetStringAsync($"/review-application/{c.Id}")).Contains("OV-C Business") &&
        !(await ownerClient.GetStringAsync($"/tracking/{c.Id}")).Contains("OV-C Business"), "Official review and applicant tracking remain scoped");
    Check(before == await Snapshot() && (await File.ReadAllBytesAsync(Path.Combine(pdfDir, "test.pdf"))).SequenceEqual(pdf), "Oversight reads and denied actions change no application/document/message data or PDF bytes");
    var later = Fixture("OV-LATER", owner, "Bergrivier Municipality", "Submitted", "Sale of Meals Licence", "New");
    db.Applications.Add(later); await db.SaveChangesAsync();
    Check((await oversight.ReportAsync(admin)).Total == 7 && (await adminClient.GetStringAsync("/admin/reports")).Contains("Provincial Application Trends Report"), "Later database additions remain available to reporting");
    Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(), "No model changes or pending migrations");
    Console.WriteLine("All Admin oversight service/HTTP checks passed. Test data: " + root);
}
finally
{
    Stop();
    // Report categories only: request logs can contain security tokens and must not be printed.
    foreach (var category in logs.Where(l => l.StartsWith("warn:") || l.StartsWith("fail:")).Distinct()) Console.WriteLine("Host diagnostic: " + category);
}
