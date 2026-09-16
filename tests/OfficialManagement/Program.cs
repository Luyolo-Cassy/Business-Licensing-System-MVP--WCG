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
var root = Path.Combine(Path.GetTempPath(), "official-management-" + Guid.NewGuid().ToString("N"));
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

try
{
    if (args.Contains("--existing-copy"))
    {
        // Exercise an exact copy of the current database; never start the host against the original.
        using var source = new SqliteConnection($"Data Source={Path.GetFullPath("businesslicensing.db")};Mode=ReadOnly");
        using var copy = new SqliteConnection($"Data Source={Path.Combine(root, "businesslicensing.db")}");
        source.Open(); copy.Open(); source.BackupDatabase(copy);
        string Snapshot()
        {
            using var tablesCommand = copy.CreateCommand();
            tablesCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            var names = new List<string>();
            using (var reader = tablesCommand.ExecuteReader()) while (reader.Read()) names.Add(reader.GetString(0));
            var rows = new List<string>();
            foreach (var name in names)
            {
                using var command = copy.CreateCommand();
                command.CommandText = "SELECT * FROM \"" + name.Replace("\"", "\"\"") + "\"";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var values = new object[reader.FieldCount]; reader.GetValues(values);
                    rows.Add(name + JsonSerializer.Serialize(values));
                }
            }
            return string.Join("\n", rows.OrderBy(row => row, StringComparer.Ordinal));
        }
        var before = Snapshot();
        await Start(); Stop(); await Start();
        Check(before == Snapshot(), "Copy of current database: every table row unchanged across two startups");
        using var currentAdmin = await Login("dedat.admin@example.test", "DevOnly!DEDAT2026#", "/admin-dashboard");
        Check((await currentAdmin.GetAsync("/admin/officials")).StatusCode == HttpStatusCode.OK, "Existing database Admin can access Official management");
        Console.WriteLine("Current database remained read-only; restart verification used a temporary copy.");
        return;
    }
    await Start();
    // Match the application's host data-protection discriminator for actual setup-page HTTP tests.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, ApplicationName = typeof(ApplicationUser).Assembly.GetName().Name });
    builder.Logging.ClearProviders();
    builder.Services.AddDataProtection();
    builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(root, "businesslicensing.db")}"));
    builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
    builder.Services.AddScoped<OfficialManagementService>(); builder.Services.AddScoped<MunicipalMessageService>();
    await using var provider = builder.Services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var management = scope.ServiceProvider.GetRequiredService<OfficialManagementService>();
    var messages = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
    var admin = Principal((await users.FindByEmailAsync("dedat.admin@example.test"))!);
    Check(await db.Roles.CountAsync() == 3 && (await management.ListAsync(admin)).Count == 0 && await db.Users.CountAsync() == 1,
        "Fresh startup creates three roles, Development Admin, and ZERO Officials");
    Stop(); await Start();
    Check((await management.ListAsync(admin)).Count == 0 && await db.Users.CountAsync() == 1, "Fresh restart still creates ZERO Officials");
    // Historical-account fixtures only: runtime startup must never provision these addresses.
    var legacyEmails = new[] {
        "official@westerncape.gov.za", "bergrivier.official@westerncape.gov.za",
        "cederberg.official@westerncape.gov.za", "hessequa.official@westerncape.gov.za",
        "swartland.official@westerncape.gov.za", "witzenberg.official@westerncape.gov.za"
    };
    foreach (var email in legacyEmails)
    {
        var fixture = new ApplicationUser { UserName = email, Email = email, FullName = "Legacy Fixture", Municipality = "Bergrivier Municipality" };
        Check((await users.CreateAsync(fixture, "Password123!")).Succeeded, "Create isolated historical fixture");
        Check((await users.AddToRoleAsync(fixture, "MunicipalOfficial")).Succeeded, "Assign fixture role");
    }
    var legacy = (await users.FindByEmailAsync("bergrivier.official@westerncape.gov.za"))!;
    Check((await management.ListAsync(admin)).Count == 6, "All six historical fixture Officials listed");
    using var adminClient = await Login("dedat.admin@example.test", "DevOnly!DEDAT2026#", "/admin-dashboard");
    var adminHtml = await adminClient.GetStringAsync("/admin/officials");
    Check(adminHtml.Contains("bergrivier.official@westerncape.gov.za") && adminHtml.Contains("official@westerncape.gov.za"), "Admin management HTTP page shows legacy Officials");
    var owner = new ApplicationUser { UserName = "owner@example.test", Email = "owner@example.test", FullName = "Test Owner" };
    Check((await users.CreateAsync(owner, "OwnerOnly!2026#")).Succeeded, "Create isolated applicant");
    await users.AddToRoleAsync(owner, "BusinessOwner");
    foreach (var principal in new[] { Principal(owner), Principal(legacy), new ClaimsPrincipal() })
    {
        await Reject<UnauthorizedAccessException>(() => management.ListAsync(principal));
        await Reject<UnauthorizedAccessException>(() => management.SaveAsync(principal, null, "No", "no@example.test", 1));
        await Reject<UnauthorizedAccessException>(() => management.SetActiveAsync(principal, legacy.Id, false));
        await Reject<UnauthorizedAccessException>(() => management.GenerateInvitationAsync(principal, legacy.Id));
    }
    using (var ownerClient = await Login(owner.Email!, "OwnerOnly!2026#", "/dashboard"))
    using (var legacyClient = await Login(legacy.Email!, "Password123!", "/official-dashboard"))
    {
        foreach (var client in new[] { ownerClient, legacyClient })
        {
            var response = await client.GetAsync("/admin/officials");
            Check(response.StatusCode == HttpStatusCode.Forbidden || response.Headers.Location?.ToString().Contains("AccessDenied") == true, "Non-admin denied management URL");
        }
    }
    var invite = (await management.SaveAsync(admin, null, "  Sarah Test  ", " sarah@example.test ", 1))!;
    db.ChangeTracker.Clear();
    var sarah = (await users.FindByIdAsync(invite.UserId))!;
    Check((await users.GetRolesAsync(sarah)).SequenceEqual(new[] { "MunicipalOfficial" }) && sarah.Municipality == "Bergrivier Municipality", "New Official has only MunicipalOfficial and one active assignment");
    Check(!await users.HasPasswordAsync(sarah) && sarah.FullName == "Sarah Test", "New Official has no password; details trimmed");
    await Reject<ValidationException>(() => management.SaveAsync(admin, null, "Duplicate", "SARAH@example.test", 1));
    await Reject<ValidationException>(() => management.SaveAsync(admin, null, "Bad", "bad-email", 1));
    await Reject<ValidationException>(() => management.SaveAsync(admin, null, "No municipality", "none@example.test", null));
    var setupUrl = QueryHelpers.AddQueryString("/Account/Setup-Official", new Dictionary<string, string?> { ["userId"] = invite.UserId, ["code"] = invite.Code });
    using var setupClient = Client();
    Check((await setupClient.GetStringAsync(setupUrl)).Contains("New Password"), "Generated setup link resolves on localhost");
    var mismatch = await Form(setupClient, setupUrl, new() { ["_handler"] = "official-setup", ["Input.Password"] = "OfficialOwn!2026#", ["Input.ConfirmPassword"] = "different" });
    Check((await mismatch.Content.ReadAsStringAsync()).Contains("do not match"), "Password confirmation validated");
    var weak = await Form(setupClient, setupUrl, new() { ["_handler"] = "official-setup", ["Input.Password"] = "a", ["Input.ConfirmPassword"] = "a" });
    Check(!(await weak.Content.ReadAsStringAsync()).Contains("Your password has been set"), "Identity rejects weak password");
    var completed = await Form(setupClient, setupUrl, new() { ["_handler"] = "official-setup", ["Input.Password"] = "OfficialOwn!2026#", ["Input.ConfirmPassword"] = "OfficialOwn!2026#" });
    Check((await completed.Content.ReadAsStringAsync()).Contains("Your password has been set"), "Official sets own password through anonymous HTTP form");
    Check(!await management.ValidateSetupAsync(invite.UserId, invite.Code), "Setup token cannot be reused");
    await Reject<ValidationException>(() => management.CompleteSetupAsync(invite.UserId, invite.Code, "Changed!2026#"));
    await Reject<ValidationException>(() => management.GenerateInvitationAsync(admin, sarah.Id));
    Check(!await management.ValidateSetupAsync(invite.UserId, "malformed!") && !await management.ValidateSetupAsync("missing", invite.Code), "Malformed and wrong-user tokens rejected");
    db.ChangeTracker.Clear(); sarah = (await users.FindByIdAsync(sarah.Id))!;
    var stale = Principal(sarah);
    var oldApp = new Application { UserId = owner.Id, Municipality = "Bergrivier Municipality", BusinessName = "OLD-MUNICIPALITY-BUSINESS", Status = "Submitted", ApplicationFormFilePath = "1/test.pdf", ApplicationFormFileName = "test.pdf" };
    var newApp = new Application { UserId = owner.Id, Municipality = "Swartland Municipality", BusinessName = "NEW-MUNICIPALITY-BUSINESS", Status = "Submitted", ApplicationFormFilePath = "1/test.pdf", ApplicationFormFileName = "test.pdf" };
    db.Applications.AddRange(oldApp, newApp); await db.SaveChangesAsync();
    var pdfDir = Path.Combine(root, "App_Data", "generated-applications", "1"); Directory.CreateDirectory(pdfDir);
    byte[] pdf = "%PDF-1.4 test bytes"u8.ToArray(); await File.WriteAllBytesAsync(Path.Combine(pdfDir, "test.pdf"), pdf);
    using var originalSession = await Login("sarah@example.test", "OfficialOwn!2026#", "/official-dashboard");
    var dashboard = await originalSession.GetStringAsync("/official-dashboard");
    Check(dashboard.Contains(oldApp.BusinessName) && !dashboard.Contains(newApp.BusinessName), "Official dashboard isolates municipality");
    Check((await originalSession.GetAsync($"/applications/{oldApp.Id}/official-pdf")).StatusCode == HttpStatusCode.OK, "Assigned PDF allowed");
    Check((await originalSession.GetAsync($"/applications/{newApp.Id}/official-pdf")).StatusCode != HttpStatusCode.OK, "Other municipality PDF denied");
    await messages.SaveReviewAsync(stale, oldApp.Id, "Under Review", "Initial review");
    await management.SaveAsync(admin, sarah.Id, "Sarah Updated", "sarah.updated@example.test", 4);
    await Reject<UnauthorizedAccessException>(() => messages.SaveReviewAsync(stale, oldApp.Id, "Licence Issued", "Stale approval"));
    Check(await OfficialAccess.GetAsync(db, stale) == null, "Stale security stamp rejected immediately after reassignment");
    Check((await originalSession.GetAsync($"/applications/{oldApp.Id}/official-pdf")).StatusCode != HttpStatusCode.OK, "Stale HTTP cookie revoked");
    using var reassignedSession = await Login("sarah.updated@example.test", "OfficialOwn!2026#", "/official-dashboard");
    dashboard = await reassignedSession.GetStringAsync("/official-dashboard");
    Check(!dashboard.Contains(oldApp.BusinessName) && dashboard.Contains(newApp.BusinessName), "Reassigned Official sees only new municipality");
    Check((await reassignedSession.GetAsync($"/applications/{oldApp.Id}/official-pdf")).StatusCode != HttpStatusCode.OK &&
        (await reassignedSession.GetAsync($"/applications/{newApp.Id}/official-pdf")).StatusCode == HttpStatusCode.OK, "PDF access follows reassignment");
    db.ChangeTracker.Clear(); sarah = (await users.FindByIdAsync(sarah.Id))!;
    var current = Principal(sarah);
    await Reject<UnauthorizedAccessException>(() => messages.SaveReviewAsync(current, oldApp.Id, "Rejected", "Wrong municipality"));
    await messages.SaveReviewAsync(current, newApp.Id, "Under Review", "New municipality review");
    await db.Municipalities.Where(m => m.Id == 4).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsActive, false));
    await Reject<ValidationException>(() => management.SaveAsync(admin, null, "Inactive", "inactive@example.test", 4));
    await Reject<ValidationException>(() => management.SaveAsync(admin, legacy.Id, legacy.FullName, legacy.Email!, 4));
    await messages.SaveReviewAsync(current, newApp.Id, "Under Review", "Historical inactive access");
    Check((await reassignedSession.GetAsync($"/applications/{newApp.Id}/official-pdf")).StatusCode == HttpStatusCode.OK, "Inactive municipality preserves existing Official access");
    await management.SetActiveAsync(admin, sarah.Id, false);
    foreach (var status in new[] { "Licence Issued", "Rejected", "Under Review" })
        await Reject<UnauthorizedAccessException>(() => messages.SaveReviewAsync(current, newApp.Id, status, "Stale action"));
    Check(await OfficialAccess.GetAsync(db, current) == null, "Deactivation blocks stale circuit authorization");
    Check((await reassignedSession.GetAsync($"/applications/{newApp.Id}/official-pdf")).StatusCode != HttpStatusCode.OK, "Deactivation blocks stale PDF session");
    using (var deniedClient = Client())
    {
        var response = await Form(deniedClient, "/Account/Login", new() { ["_handler"] = "login", ["Input.Email"] = "sarah.updated@example.test", ["Input.Password"] = "OfficialOwn!2026#" });
        Check(response.Headers.Location?.ToString().Contains("Lockout") == true, "Deactivated Official cannot log in");
    }
    await management.SetActiveAsync(admin, sarah.Id, true);
    using (var reactivated = await Login("sarah.updated@example.test", "OfficialOwn!2026#", "/official-dashboard"))
        Check((await reactivated.GetAsync($"/applications/{newApp.Id}/official-pdf")).StatusCode == HttpStatusCode.OK, "Reactivation preserves password and assignment");
    await management.SaveAsync(admin, legacy.Id, "Edited Legacy Official", "legacy.edited@example.test", 3);
    // These deliberate fixture changes must not be undone by startup. No real users are changed.
    var deletedFixture = (await users.FindByEmailAsync(legacyEmails[2]))!;
    Check((await users.DeleteAsync(deletedFixture)).Succeeded, "Delete isolated fixture to test non-recreation");
    var roleRemovedFixture = (await users.FindByEmailAsync(legacyEmails[3]))!;
    Check((await users.RemoveFromRoleAsync(roleRemovedFixture, "MunicipalOfficial")).Succeeded, "Remove isolated fixture role to test non-restoration");
    var capeFixture = (await users.FindByEmailAsync(legacyEmails[0]))!;
    capeFixture.Municipality = null; Check((await users.UpdateAsync(capeFixture)).Succeeded, "Clear isolated legacy assignment to test non-restoration");
    var beforeRestart = JsonSerializer.Serialize(await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync());
    Stop(); await Start();
    Check(beforeRestart == JsonSerializer.Serialize(await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync()), "Restart leaves every existing user detail unchanged and recreates no deleted user");
    Check(!await users.IsInRoleAsync(roleRemovedFixture, "MunicipalOfficial"), "Restart does not restore a predefined email's role");
    using (var persistent = await Login("sarah.updated@example.test", "OfficialOwn!2026#", "/official-dashboard"))
        Check((await persistent.GetAsync($"/applications/{newApp.Id}/official-pdf")).StatusCode == HttpStatusCode.OK, "Admin-created Official persists across restart");
    using var legacyEdited = await Login("legacy.edited@example.test", "Password123!", "/official-dashboard");
    db.ChangeTracker.Clear();
    var persistedLegacy = (await users.FindByIdAsync(legacy.Id))!;
    Check(persistedLegacy.Municipality == "Hessequa Municipality" && persistedLegacy.FullName == "Edited Legacy Official" && (await management.ListAsync(admin)).Count == 5, "Legacy edits survive restart without duplicate seed accounts");
    Check(await db.Applications.CountAsync() == 2 && await db.MunicipalMessages.CountAsync() == 3 &&
        await db.Applications.AnyAsync(a => a.Id == oldApp.Id && a.Municipality == "Bergrivier Municipality") &&
        await db.Applications.AnyAsync(a => a.Id == newApp.Id && a.Municipality == "Swartland Municipality") &&
        (await File.ReadAllBytesAsync(Path.Combine(pdfDir, "test.pdf"))).SequenceEqual(pdf), "Historical applications/messages/PDF bytes preserved; applications never reassigned");
    Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(), "No pending model changes or migrations");
    Console.WriteLine("All Official management integration/HTTP checks passed. Test data: " + root);
}
finally
{
    Stop();
    // Intentionally do not print request logs, which could contain setup URLs.
}
