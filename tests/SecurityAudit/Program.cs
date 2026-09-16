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
var root = Path.Combine(Path.GetTempPath(), "security-audit-" + Guid.NewGuid().ToString("N"));
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
        "--Logging:LogLevel:Default", "Warning", "--Logging:LogLevel:Microsoft.AspNetCore", "Warning" }) start.ArgumentList.Add(arg);
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

// Explicit operator action, separate from automated tests. No database is opened.
if (args.Contains("--migrate-existing-uploads"))
{
    var workspace = Path.GetFullPath(Environment.CurrentDirectory);
    var legacy = Path.Combine(workspace, "wwwroot", "uploads");
    var destination = Path.Combine(workspace, "App_Data", "protected-uploads");
    var files = Directory.Exists(legacy) ? Directory.GetFiles(legacy, "*", SearchOption.AllDirectories) : [];
    var hashes = files.ToDictionary(f => Path.GetRelativePath(legacy, f), f => System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)));
    var migrationBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = workspace, WebRootPath = Path.Combine(workspace, "wwwroot") });
    var uploads = new ProtectedUploadService(migrationBuilder.Environment);
    var moved = uploads.MoveLegacyUploads();
    Check(hashes.All(f => File.Exists(Path.Combine(destination, f.Key)) && System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(destination, f.Key))).SequenceEqual(f.Value)), "All moved files retain identical SHA-256 hashes");
    Check(!Directory.Exists(legacy) || Directory.GetFiles(legacy, "*", SearchOption.AllDirectories).Length == 0, "No private document files remain under wwwroot/uploads");
    Console.WriteLine($"Moved {moved} files from {legacy} to {destination}. No database changes.");
    return;
}

try
{
    var legacyDir = Path.Combine(root, "wwwroot", "uploads"); Directory.CreateDirectory(legacyDir);
    byte[] documentBytes = "%PDF-1.4 private supporting document fixture"u8.ToArray();
    foreach (var file in new[] { "owner.pdf", "other.pdf", "legacy-form.pdf", "legacy-document.pdf", "orphan.pdf" })
        await File.WriteAllBytesAsync(Path.Combine(legacyDir, file), documentBytes);
    await Start();
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, WebRootPath = Path.Combine(root, "wwwroot"), ApplicationName = typeof(ApplicationUser).Assembly.GetName().Name });
    builder.Logging.ClearProviders(); builder.Services.AddDataProtection();
    builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(root, "businesslicensing.db")}"));
    builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
    builder.Services.AddScoped<OfficialManagementService>(); builder.Services.AddScoped<MunicipalMessageService>();
    builder.Services.AddScoped<ApplicantApplicationService>(); builder.Services.AddScoped<MunicipalityManagementService>();
    await using var provider = builder.Services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var management = scope.ServiceProvider.GetRequiredService<OfficialManagementService>();
    var messages = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
    var applicantActions = scope.ServiceProvider.GetRequiredService<ApplicantApplicationService>();
    var admin = Principal((await users.FindByEmailAsync("dedat.admin@example.test"))!);
    var uploads = new ProtectedUploadService(builder.Environment);
    Check(Directory.GetFiles(legacyDir).Length == 0 && (await File.ReadAllBytesAsync(Path.Combine(root, "App_Data", "protected-uploads", "owner.pdf"))).SequenceEqual(documentBytes), "Startup moves legacy uploads outside wwwroot without changing bytes");
    Check(uploads.MoveLegacyUploads() == 0, "Legacy file move is idempotent");
    await File.WriteAllBytesAsync(Path.Combine(legacyDir, "owner.pdf"), "collision"u8.ToArray());
    await Reject<InvalidOperationException>(() => Task.Run(() => uploads.MoveLegacyUploads()));
    Check((await File.ReadAllBytesAsync(Path.Combine(root, "App_Data", "protected-uploads", "owner.pdf"))).SequenceEqual(documentBytes), "Collision fails safely without overwriting a protected document");
    // This is the deliberately conflicting temporary fixture only; no real documents are deleted.
    File.Move(Path.Combine(legacyDir, "owner.pdf"), Path.Combine(root, "collision-fixture.pdf"));
    var newReference = await uploads.SaveAsync("New document.pdf", documentBytes);
    Check(File.Exists(Path.Combine(root, "App_Data", "protected-uploads", newReference.Split('/').Last())) && Directory.GetFiles(legacyDir).Length == 0, "New uploads are private from creation");
    async Task<ApplicationUser> Owner(string name)
    {
        var user = new ApplicationUser { UserName = name + "@example.test", Email = name + "@example.test", FullName = name };
        Check((await users.CreateAsync(user, "OwnerOnly!2026#")).Succeeded, "Create isolated owner");
        Check((await users.AddToRoleAsync(user, "BusinessOwner")).Succeeded, "Assign BusinessOwner"); return user;
    }
    var owner = await Owner("owner"); var other = await Owner("other");
    async Task<ApplicationUser> Official(string email, int municipality)
    {
        var invite = (await management.SaveAsync(admin, null, "Audit Official", email, municipality))!;
        await management.CompleteSetupAsync(invite.UserId, invite.Code, "OfficialOwn!2026#");
        return (await users.FindByIdAsync(invite.UserId))!;
    }
    var official = await Official("audit.one@example.test", 1); var otherOfficial = await Official("audit.two@example.test", 4);
    var ownApplication = new Application { UserId = owner.Id, Municipality = "Bergrivier Municipality", Status = "Submitted", BusinessName = "OWNER-PRIVATE-BUSINESS", ApplicationNumber = "SEC-OWN",
        ApplicationFormFilePath = "1/test.pdf", ApplicationFormFileName = "test.pdf", DateSubmitted = DateTime.Now,
        Documents = [new ApplicationDocument { FilePath = "/uploads/owner.pdf", FileName = "owner.pdf" }, new ApplicationDocument { FilePath = newReference, FileName = "New document.pdf" }] };
    var otherApplication = new Application { UserId = other.Id, Municipality = "Swartland Municipality", Status = "Submitted", BusinessName = "OTHER-PRIVATE-BUSINESS", ApplicationNumber = "SEC-OTHER",
        ApplicationFormFilePath = "2/test.pdf", ApplicationFormFileName = "test.pdf", DateSubmitted = DateTime.Now,
        Documents = [new ApplicationDocument { FilePath = "/uploads/other.pdf", FileName = "other.pdf" }] };
    var legacyApplication = new Application { UserId = owner.Id, Municipality = "Bergrivier Municipality", Status = "Submitted", ApplicationFormFilePath = "/uploads/legacy-form.pdf", UploadedDocumentPath = "/uploads/legacy-document.pdf" };
    db.Applications.AddRange(ownApplication, otherApplication, legacyApplication); await db.SaveChangesAsync();
    foreach (var number in new[] { "1", "2" })
    {
        var pdfDir = Path.Combine(root, "App_Data", "generated-applications", number); Directory.CreateDirectory(pdfDir);
        await File.WriteAllBytesAsync(Path.Combine(pdfDir, "test.pdf"), documentBytes);
    }
    using var adminClient = await Login("dedat.admin@example.test", "DevOnly!DEDAT2026#", "/admin-dashboard");
    using var ownerClient = await Login(owner.Email!, "OwnerOnly!2026#", "/dashboard");
    using var otherClient = await Login(other.Email!, "OwnerOnly!2026#", "/dashboard");
    using var officialClient = await Login(official.Email!, "OfficialOwn!2026#", "/official-dashboard");
    using var otherOfficialClient = await Login(otherOfficial.Email!, "OfficialOwn!2026#", "/official-dashboard");
    using var anonymous = Client();
    async Task Download(HttpClient client, string url, bool allowed)
    {
        var response = await client.GetAsync(url);
        Check(allowed ? response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsByteArrayAsync()).SequenceEqual(documentBytes) : response.StatusCode != HttpStatusCode.OK,
            $"Document/PDF {(allowed ? "allowed" : "denied")}: {url}");
        if (allowed) Check(response.Headers.CacheControl?.NoStore == true, "Private download prevents caching");
    }
    foreach (var url in new[] { "/uploads/owner.pdf", newReference, "/uploads/legacy-form.pdf", "/uploads/legacy-document.pdf", $"/applications/{ownApplication.Id}/official-pdf" })
    {
        await Download(ownerClient, url, true); await Download(officialClient, url, true); await Download(adminClient, url, true);
        await Download(otherClient, url, false); await Download(otherOfficialClient, url, false); await Download(anonymous, url, false);
    }
    await Download(otherClient, "/uploads/other.pdf", true); await Download(otherOfficialClient, "/uploads/other.pdf", true); await Download(adminClient, "/uploads/other.pdf", true);
    foreach (var url in new[] { "/uploads/orphan.pdf", "/uploads/owner.fingerprint.pdf", "/uploads/%2e%2e%2fappsettings.json", "/uploads/owner.pdf%3aanything", "/App_Data/protected-uploads/owner.pdf" })
        await Download(adminClient, url, false);
    foreach (var client in new[] { ownerClient, otherClient, officialClient, otherOfficialClient, anonymous })
        foreach (var url in new[] { "/admin-dashboard", "/admin/applications", $"/admin/applications/{ownApplication.Id}", "/admin/reports", "/admin/municipalities", "/admin/officials" })
            Check((await client.GetAsync(url)).StatusCode != HttpStatusCode.OK, "Non-admin denied " + url);
    foreach (var url in new[] { "/admin-dashboard", "/admin/applications", $"/admin/applications/{ownApplication.Id}", "/admin/reports", "/admin/municipalities", "/admin/officials" })
        Check((await adminClient.GetAsync(url)).StatusCode == HttpStatusCode.OK, "Admin allowed " + url);
    foreach (var client in new[] { ownerClient, adminClient, anonymous })
        foreach (var url in new[] { "/official-dashboard", "/generate-report", $"/review-application/{ownApplication.Id}" })
            Check((await client.GetAsync(url)).StatusCode != HttpStatusCode.OK, "Non-official denied " + url);
    foreach (var client in new[] { officialClient, adminClient, anonymous })
        Check((await client.GetAsync($"/tracking/{ownApplication.Id}")).StatusCode != HttpStatusCode.OK, "Non-applicant denied tracking");
    Check((await ownerClient.GetStringAsync($"/tracking/{ownApplication.Id}")).Contains("SEC-OWN") &&
        !(await ownerClient.GetStringAsync($"/tracking/{otherApplication.Id}")).Contains("SEC-OTHER"), "Applicant manipulated IDs do not disclose another owner's application");
    Check((await officialClient.GetStringAsync($"/review-application/{ownApplication.Id}")).Contains("OWNER-PRIVATE-BUSINESS") &&
        !(await officialClient.GetStringAsync($"/review-application/{otherApplication.Id}")).Contains("OTHER-PRIVATE-BUSINESS"), "Official manipulated IDs remain municipality-scoped");
    foreach (var status in new[] { "Licence Issued", "Rejected", "Under Review" })
    {
        await messages.SaveReviewAsync(Principal(official), ownApplication.Id, status, "Authorized review");
        foreach (var denied in new[] { Principal(otherOfficial), Principal(owner), admin, new ClaimsPrincipal() })
            await Reject<UnauthorizedAccessException>(() => messages.SaveReviewAsync(denied, ownApplication.Id, status, "Denied"));
    }
    Check((await messages.GetMessagesAsync(Principal(other), ownApplication.Id)).Count == 0 && (await messages.GetMessagesAsync(Principal(owner), ownApplication.Id)).Count == 3, "Applicant messages respect ownership");
    await Reject<UnauthorizedAccessException>(() => applicantActions.ChangeSubmissionAsync(Principal(other), ownApplication.Id, false));
    await Reject<UnauthorizedAccessException>(() => applicantActions.ChangeSubmissionAsync(Principal(official), ownApplication.Id, false));
    await Reject<UnauthorizedAccessException>(() => applicantActions.ChangeSubmissionAsync(admin, ownApplication.Id, false));
    await Reject<ValidationException>(() => applicantActions.ChangeSubmissionAsync(Principal(owner), ownApplication.Id, false));
    await Reject<ValidationException>(() => applicantActions.ChangeSubmissionAsync(Principal(owner), ownApplication.Id, true));
    Check(true, "Stale applicant withdrawal/reapplication cannot overwrite Official decisions");
    await messages.SaveReviewAsync(Principal(official), ownApplication.Id, "Rejected", "Reapplication fixture");
    await db.Municipalities.Where(m => m.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsActive, false));
    await Reject<ValidationException>(() => applicantActions.ChangeSubmissionAsync(Principal(owner), ownApplication.Id, true));
    await Download(officialClient, "/uploads/owner.pdf", true);
    await db.Municipalities.Where(m => m.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsActive, true));
    await applicantActions.ChangeSubmissionAsync(Principal(owner), ownApplication.Id, true);
    await applicantActions.ChangeSubmissionAsync(Principal(owner), ownApplication.Id, false);
    Check((await db.Applications.AsNoTracking().SingleAsync(a => a.Id == ownApplication.Id)).Status == "Withdrawn", "Valid owner reapplication and withdrawal still work");
    await management.SaveAsync(admin, official.Id, official.FullName, official.Email!, 4);
    await Download(officialClient, "/uploads/owner.pdf", false);
    await Reject<UnauthorizedAccessException>(() => messages.SaveReviewAsync(Principal(official), ownApplication.Id, "Licence Issued", "Stale"));
    using var reassigned = await Login(official.Email!, "OfficialOwn!2026#", "/official-dashboard");
    await Download(reassigned, "/uploads/owner.pdf", false); await Download(reassigned, "/uploads/other.pdf", true);
    await management.SetActiveAsync(admin, official.Id, false);
    await Download(reassigned, "/uploads/other.pdf", false);
    await management.SetActiveAsync(admin, official.Id, true);
    using var reactivated = await Login(official.Email!, "OfficialOwn!2026#", "/official-dashboard");
    await Download(reactivated, "/uploads/other.pdf", true);
    // Removing the applicant role must also remove document ownership privileges.
    Check((await users.RemoveFromRoleAsync(owner, "BusinessOwner")).Succeeded, "Remove isolated applicant role");
    await Download(ownerClient, "/uploads/owner.pdf", false); await Download(ownerClient, $"/applications/{ownApplication.Id}/official-pdf", false);
    Check((await anonymous.GetAsync("/icons/phosphor/house.svg")).StatusCode == HttpStatusCode.OK, "Unrelated static icons remain public");
    using var registrationClient = Client();
    var registration = await Form(registrationClient, "/Account/Register", new()
    {
        ["_handler"] = "register", ["Input.FullName"] = "Self Registered Applicant",
        ["Input.Email"] = "self-registration@example.test", ["Input.Password"] = "OwnerTest!2026#",
        ["Input.ConfirmPassword"] = "OwnerTest!2026#"
    });
    var registered = await users.FindByEmailAsync("self-registration@example.test");
    Check(registration.StatusCode == HttpStatusCode.Redirect && registered != null &&
        (await users.GetRolesAsync(registered)).SequenceEqual(new[] { "BusinessOwner" }),
        "Normal self-registration assigns only BusinessOwner");
    Check(await db.Applications.CountAsync() == 3 && await db.ApplicationDocuments.CountAsync() == 3 && await db.MunicipalMessages.CountAsync() == 4,
        "Security checks preserve historical application/document/message records");
    Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(), "No schema change or pending migration");
    Console.WriteLine("All focused security checks passed. Temporary data: " + root);
}
finally
{
    Stop();
    foreach (var category in logs.Where(l => l.StartsWith("warn:") || l.StartsWith("fail:")).Distinct()) Console.WriteLine("Host diagnostic: " + category);
}
