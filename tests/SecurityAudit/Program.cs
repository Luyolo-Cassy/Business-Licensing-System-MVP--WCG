using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.RegularExpressions;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using BusinessLicensing_Practice.Services.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

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
if (args.Contains("--local-private-file-store"))
{
    try
    {
        var storageBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root });
        var store = new LocalPrivateFileStore(storageBuilder.Environment);
        var key = PrivateFileKeys.ProtectedUpload("fixture.pdf");
        var bytes = "%PDF-1.4 local private file store fixture"u8.ToArray();
        await store.SaveAsync(key, bytes, "application/pdf");
        Check(await store.ExistsAsync(key), "Local store finds a saved object");
        Check((await store.ReadAsync(key))!.SequenceEqual(bytes), "Local store reads unchanged object bytes");
        var opened = await store.OpenReadAsync(key);
        Check(opened?.Content.CanRead == true, "Local store opens a readable object stream");
        if (opened != null) await opened.Content.DisposeAsync();
        Check(File.Exists(Path.Combine(root, "App_Data", "protected-uploads", "fixture.pdf")),
            "Protected-upload keys retain the existing App_Data location");
        await store.DeleteAsync(key);
        Check(!await store.ExistsAsync(key), "Local store deletes an object");
        try
        {
            await store.SaveAsync("../outside.pdf", bytes, "application/pdf");
            throw new Exception("Local store accepted a traversal key");
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("PASS: Local store rejects path traversal");
        }
    }
    finally
    {
        try { Directory.Delete(root, true); } catch { }
    }
    return;
}
if (args.Contains("--r2-configuration"))
{
    try
    {
        static List<ValidationResult> Validate(CloudflareR2Options options)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return results;
        }

        Check(Validate(new CloudflareR2Options()).Count >= 4,
            "R2 configuration requires endpoint, bucket name, access key ID, and secret access key");
        Check(Validate(new CloudflareR2Options
        {
            Endpoint = "https://example.r2.cloudflarestorage.com",
            BucketName = "private-files",
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key"
        }).Count == 0, "R2 configuration accepts a complete HTTPS S3 configuration");
        Check(Validate(new CloudflareR2Options
        {
            Endpoint = "http://example.r2.cloudflarestorage.com?unsafe=true",
            BucketName = "folder/private-files",
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key"
        }).Count == 2, "R2 configuration rejects an unsafe endpoint and bucket path");
    }
    finally
    {
        try { Directory.Delete(root, true); } catch { }
    }
    return;
}
if (args.Contains("--r2-integration"))
{
    var connectionSucceeded = false;
    var uploadSucceeded = false;
    var existsSucceeded = false;
    var bufferedReadSucceeded = false;
    var streamedReadSucceeded = false;
    var overwriteSucceeded = false;
    var deleteSucceeded = false;
    var missingHandlingSucceeded = false;
    var cleanupSucceeded = false;
    string? errorType = null;
    string? errorStage = null;
    var stage = "configuration";

    var configuration = new ConfigurationBuilder()
        .AddUserSecrets(typeof(CloudflareR2Options).Assembly, optional: false)
        .Build();
    var r2Options = configuration.GetSection(CloudflareR2Options.SectionName).Get<CloudflareR2Options>()
        ?? throw new InvalidOperationException("R2 configuration is missing.");
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(r2Options, new ValidationContext(r2Options), validationResults,
            validateAllProperties: true))
        throw new InvalidOperationException("R2 configuration is incomplete or invalid.");

    using var s3Client = new AmazonS3Client(
        new BasicAWSCredentials(r2Options.AccessKeyId, r2Options.SecretAccessKey),
        new AmazonS3Config
        {
            ServiceURL = r2Options.Endpoint.TrimEnd('/'),
            AuthenticationRegion = "auto",
            ForcePathStyle = true
        });
    var store = new CloudflareR2PrivateFileStore(s3Client, Options.Create(r2Options));
    var objectKey = $"integration-tests/r2-storage-test-{Guid.NewGuid():N}.txt";
    var original = Encoding.UTF8.GetBytes("Cloudflare R2 private storage integration test.");
    var replacement = Encoding.UTF8.GetBytes("Cloudflare R2 private storage overwrite test.");

    try
    {
        stage = "upload";
        await store.SaveAsync(objectKey, original, "text/plain");
        connectionSucceeded = uploadSucceeded = true;

        stage = "existence check";
        existsSucceeded = await store.ExistsAsync(objectKey);
        if (!existsSucceeded) throw new InvalidOperationException("The uploaded temporary object was not found.");

        stage = "buffered read";
        bufferedReadSucceeded = (await store.ReadAsync(objectKey))?.SequenceEqual(original) == true;
        if (!bufferedReadSucceeded) throw new InvalidOperationException("Buffered content did not match.");

        stage = "streamed read";
        var opened = await store.OpenReadAsync(objectKey);
        if (opened != null)
        {
            await using var stream = opened.Content;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            streamedReadSucceeded = buffer.ToArray().SequenceEqual(original);
        }
        if (!streamedReadSucceeded) throw new InvalidOperationException("Streamed content did not match.");

        stage = "overwrite";
        await store.SaveAsync(objectKey, replacement, "text/plain");
        overwriteSucceeded = (await store.ReadAsync(objectKey))?.SequenceEqual(replacement) == true;
        if (!overwriteSucceeded) throw new InvalidOperationException("Overwritten content did not match.");

        stage = "delete";
        await store.DeleteAsync(objectKey);
        deleteSucceeded = !await store.ExistsAsync(objectKey);
        if (!deleteSucceeded) throw new InvalidOperationException("The temporary object still exists after deletion.");

        stage = "missing-object read";
        missingHandlingSucceeded = await store.ReadAsync(objectKey) == null &&
                                   await store.OpenReadAsync(objectKey) == null;
        if (!missingHandlingSucceeded)
            throw new InvalidOperationException("A missing temporary object did not return the expected result.");
    }
    catch (Exception exception)
    {
        errorType = exception.GetType().Name;
        errorStage = stage;
    }
    finally
    {
        try
        {
            await store.DeleteAsync(objectKey);
            cleanupSucceeded = !await store.ExistsAsync(objectKey);
        }
        catch (Exception exception)
        {
            cleanupSucceeded = false;
            errorType ??= exception.GetType().Name;
            errorStage ??= "cleanup";
        }
    }

    Console.WriteLine($"Connection/authentication succeeded: {connectionSucceeded}");
    Console.WriteLine($"Upload succeeded: {uploadSucceeded}");
    Console.WriteLine($"Existence/HEAD succeeded: {existsSucceeded}");
    Console.WriteLine($"Buffered read succeeded: {bufferedReadSucceeded}");
    Console.WriteLine($"Streamed read succeeded: {streamedReadSucceeded}");
    Console.WriteLine($"Overwrite succeeded: {overwriteSucceeded}");
    Console.WriteLine($"Delete succeeded: {deleteSucceeded}");
    Console.WriteLine($"Missing-object handling succeeded: {missingHandlingSucceeded}");
    Console.WriteLine($"Cleanup succeeded: {cleanupSucceeded}");
    if (errorType != null)
        Console.WriteLine($"Error: {errorType} during {errorStage}; provider message withheld to protect configuration values.");

    try { Directory.Delete(root, true); } catch { }
    Environment.ExitCode = errorType == null && cleanupSucceeded ? 0 : 1;
    return;
}
async Task Start()
{
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in new[] { appDll, "--contentRoot", root, "--urls", baseUrl, "--environment", "Development",
        "--Email:Enabled", "false",
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

// Explicit operator action, separate from automated tests. No database is opened.
if (args.Contains("--migrate-existing-uploads"))
{
    var workspace = Path.GetFullPath(Environment.CurrentDirectory);
    var legacy = Path.Combine(workspace, "wwwroot", "uploads");
    var destination = Path.Combine(workspace, "App_Data", "protected-uploads");
    var files = Directory.Exists(legacy) ? Directory.GetFiles(legacy, "*", SearchOption.AllDirectories) : [];
    var hashes = files.ToDictionary(f => Path.GetRelativePath(legacy, f), f => System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)));
    var migrationBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = workspace, WebRootPath = Path.Combine(workspace, "wwwroot") });
    var uploads = new ProtectedUploadService(new LocalPrivateFileStore(migrationBuilder.Environment), migrationBuilder.Environment);
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
    builder.Services.AddSingleton<IPrivateFileStore, LocalPrivateFileStore>();
    builder.Services.AddSingleton<ProtectedUploadService>(); builder.Services.AddScoped<ApplicationDraftService>();
    await using var provider = builder.Services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var management = scope.ServiceProvider.GetRequiredService<OfficialManagementService>();
    var messages = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
    var applicantActions = scope.ServiceProvider.GetRequiredService<ApplicantApplicationService>();
    var draftService = scope.ServiceProvider.GetRequiredService<ApplicationDraftService>();
    var admin = Principal((await users.FindByEmailAsync("dedat.admin@example.test"))!);
    var uploads = new ProtectedUploadService(new LocalPrivateFileStore(builder.Environment), builder.Environment);
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
    var incompleteDraft = new ApplicationDraftPayload
    {
        LicenceType = "Sale of Meals Licence", BusinessName = "Half-entered business",
        PlaceOfBusinessAddressLine1 = "12 Incomplete", TradingDays = []
    };
    var firstDraft = await draftService.SaveAsync(Principal(owner), incompleteDraft, 3);
    incompleteDraft.BusinessName = "Restored business";
    var updatedDraft = await draftService.SaveAsync(Principal(owner), incompleteDraft, 4);
    Check(firstDraft.Id == updatedDraft.Id && await db.ApplicationDrafts.CountAsync(item => item.UserId == owner.Id) == 1,
        "One active draft per BusinessOwner is reused");
    var restoredDraft = await draftService.GetAsync(Principal(owner));
    var restoredPayload = ApplicationDraftService.Deserialize(restoredDraft!.PayloadJson);
    Check(restoredDraft.CurrentStep == 4 && restoredPayload.BusinessName == "Restored business" &&
        restoredPayload.PlaceOfBusinessCity == "", "Incomplete draft state and CurrentStep save and restore");
    Check(await draftService.GetAsync(Principal(other)) == null, "Another BusinessOwner cannot read an owner's draft");
    await Reject<UnauthorizedAccessException>(() => draftService.GetAsync(Principal(official)));
    await Reject<UnauthorizedAccessException>(() => draftService.GetAsync(admin));
    await Reject<UnauthorizedAccessException>(() => draftService.GetAsync(new ClaimsPrincipal()));
    Check(true, "Municipal Officials, DEDAT Admins and anonymous users cannot access drafts");
    var draftReference = await uploads.SaveAsync("draft-owner.pdf", documentBytes);
    await draftService.ReplaceDocumentAsync(Principal(owner), "Owner ID Document", "draft-owner.pdf", draftReference, AiDocumentValidationStatus.Match);
    Check((await draftService.GetAsync(Principal(owner)))!.Documents.Single().FilePath == draftReference,
        "Draft document is associated with the owner's draft");
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
    await Download(ownerClient, draftReference, true);
    await Download(otherClient, draftReference, false);
    await Download(officialClient, draftReference, false);
    await Download(adminClient, draftReference, false);
    await Download(anonymous, draftReference, false);
    Check(true, "Draft documents are owner-only");
    Check((await ownerClient.GetStringAsync("/")).Contains("Continue your application") &&
        (await ownerClient.GetStringAsync("/dashboard")).Contains("Draft Application"),
        "Applicant Home and Dashboard expose the active draft separately");
    Check((await ownerClient.GetStringAsync("/new-application")).Contains("You have an application in progress") &&
        (await ownerClient.GetStringAsync("/new-application?resume=true")).Contains("Step 4 of 7"),
        "New Application explains the existing draft choice and the resume route restores saved state");

    var promotedPath = draftReference;
    var promotionDraft = await db.ApplicationDrafts.Include(item => item.Documents).SingleAsync(item => item.UserId == owner.Id);
    var promotedApplication = new Application
    {
        UserId = owner.Id, Status = "Submitted", ApplicationNumber = "SEC-PROMOTED", DateSubmitted = DateTime.Now,
        Documents = promotionDraft.Documents.Select(item => new ApplicationDocument
            { DocumentType = item.DocumentType, FileName = item.FileName, FilePath = item.FilePath }).ToList()
    };
    db.Applications.Add(promotedApplication);
    db.ApplicationDrafts.Remove(promotionDraft);
    await db.SaveChangesAsync();
    Check(await db.ApplicationDrafts.AllAsync(item => item.UserId != owner.Id) &&
        await db.ApplicationDocuments.AnyAsync(item => item.ApplicationId == promotedApplication.Id && item.FilePath == promotedPath) &&
        File.Exists(Path.Combine(root, "App_Data", "protected-uploads", promotedPath.Split('/').Last())),
        "Draft submission reuses one protected file and removes only draft records");

    var deletableReference = await uploads.SaveAsync("delete-with-draft.pdf", documentBytes);
    await draftService.SaveAsync(Principal(owner), new ApplicationDraftPayload { LicenceType = "Hawker Licence" }, 2);
    await draftService.ReplaceDocumentAsync(Principal(owner), "Proof of Address", "delete-with-draft.pdf", deletableReference, null);
    await draftService.DeleteAsync(Principal(owner));
    Check(!await db.ApplicationDrafts.AnyAsync(item => item.UserId == owner.Id) &&
        !File.Exists(Path.Combine(root, "App_Data", "protected-uploads", deletableReference.Split('/').Last())) &&
        File.Exists(Path.Combine(root, "App_Data", "protected-uploads", promotedPath.Split('/').Last())),
        "Deleting a draft removes its records and draft-only file without deleting a submitted file");
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
    Check(await db.Applications.CountAsync() == 4 && await db.ApplicationDocuments.CountAsync() == 4 && await db.MunicipalMessages.CountAsync() == 4,
        "Security checks preserve historical application/document/message records");
    Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(), "No schema change or pending migration");
    Console.WriteLine("All focused security checks passed. Temporary data: " + root);
}
finally
{
    Stop();
    foreach (var category in logs.Where(l => l.StartsWith("warn:") || l.StartsWith("fail:")).Distinct()) Console.WriteLine("Host diagnostic: " + category);
}
