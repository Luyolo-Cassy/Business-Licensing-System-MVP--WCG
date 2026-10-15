using System.Security.Claims;
using BusinessLicensing_Practice.Components.Pages;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("Failed: " + name); checks++; Console.WriteLine("PASS: " + name); }
var bytes = new byte[] { 1, 2, 3 };
var expected = new AiDocumentExpectedValues("Thandi", "Ndlovu", "Cape Foods (Pty) Ltd", "2024/123456/07", "10 Long Street", "", "Central", "Cape Town", "8001");
var fake = new FakeExtractor(); var policy = new AiDocumentValidationPolicy(fake);

var disabled = await policy.EvaluateAsync(false, "Owner ID Document", "owner.pdf", bytes, expected);
Check(disabled.Status == AiDocumentValidationStatus.Disabled && fake.Calls == 0, "disabled makes no Gemini call");
var tax = await policy.EvaluateAsync(true, "Tax Clearance Certificate", "tax.pdf", bytes, expected);
Check(tax.Status == AiDocumentValidationStatus.Disabled && fake.Calls == 0, "tax clearance makes no Gemini call");

fake.Next = new("South African Smart ID", "Extracted");
var owner = await policy.EvaluateAsync(true, "Owner ID Document", "owner.pdf", bytes, expected);
Check(owner.Status == AiDocumentValidationStatus.Match && policy.IsCurrent(owner.StoredValue, "Owner ID Document", expected, out var ownerStatus) && ownerStatus == AiDocumentValidationStatus.Match, "owner match and fingerprint reuse");
Check(policy.IsCurrent(owner.StoredValue, "Owner ID Document", expected with { ApplicantLastName = "Dlamini" }, out _), "owner result is independent of applicant name");
fake.Next = new("Passport", "Extracted");
Check((await policy.EvaluateAsync(true, "Owner ID Document", "owner.pdf", bytes, expected)).Status == AiDocumentValidationStatus.Match, "passport type matches without name extraction");
fake.Next = new("Other", "Extracted");
Check((await policy.EvaluateAsync(true, "Owner ID Document", "owner.pdf", bytes, expected)).Status == AiDocumentValidationStatus.NoMatch, "unsupported identity-document type blocks");
fake.Next = new("Unknown", "Unclear");
Check((await policy.EvaluateAsync(true, "Owner ID Document", "owner.pdf", bytes, expected)).Status == AiDocumentValidationStatus.UnableToVerify, "ambiguous identity document is inconclusive");

fake.Next = new("Certificate of Incorporation", "Extracted", BusinessName: "Cape Foods Proprietary Limited", RegistrationNumber: "2024 123456 07");
Check((await policy.EvaluateAsync(true, "Certificate of Incorporation", "company.pdf", bytes, expected)).Status == AiDocumentValidationStatus.Match, "company suffix and registration formatting normalize");
fake.Next = fake.Next with { RegistrationNumber = "2024/123456/08" };
Check((await policy.EvaluateAsync(true, "Certificate of Incorporation", "company.pdf", bytes, expected)).Status == AiDocumentValidationStatus.NoMatch, "registration mismatch blocks");
fake.Next = new("Other", "Unclear");
Check((await policy.EvaluateAsync(true, "Certificate of Incorporation", "identity.pdf", bytes, expected)).Status == AiDocumentValidationStatus.NoMatch, "clearly unrelated certificate document blocks even when certificate fields are absent");
fake.Next = new("Unknown", "Unclear");
Check((await policy.EvaluateAsync(true, "Certificate of Incorporation", "ambiguous.pdf", bytes, expected)).Status == AiDocumentValidationStatus.UnableToVerify, "ambiguous certificate evidence allows manual review");
fake.Next = null;
Check((await policy.EvaluateAsync(true, "Certificate of Incorporation", "company.pdf", bytes, expected)).Status == AiDocumentValidationStatus.UnableToVerify, "certificate technical failure allows manual review");

fake.Next = new("Proof of Address", "Extracted", AddressLine1: "10 Long St", City: "Cape Town", PostalCode: "8001", AddressContext: "RelevantPhysicalAddress");
Check((await policy.EvaluateAsync(true, "Proof of Address", "address.pdf", bytes, expected)).Status == AiDocumentValidationStatus.Match, "street abbreviation matches");
fake.Next = fake.Next with { AddressLine1 = "99 Short Road" };
Check((await policy.EvaluateAsync(true, "Proof of Address", "address.pdf", bytes, expected)).Status == AiDocumentValidationStatus.NoMatch, "different address blocks");
fake.Next = fake.Next with { AddressContext = "Ambiguous" };
Check((await policy.EvaluateAsync(true, "Proof of Address", "address.pdf", bytes, expected)).Status == AiDocumentValidationStatus.UnableToVerify, "ambiguous address allows manual review");
Check(!policy.IsCurrent("not-json", "Owner ID Document", expected, out _), "malformed payload is unvalidated");
Check(!policy.IsCurrent("Match", "Owner ID Document", expected, out _), "legacy status is readable but stale");

var handler = new CountingHandler();
var noKey = new GeminiDocumentValidationService(new HttpClient(handler), new ConfigurationBuilder().Build(), NullLogger<GeminiDocumentValidationService>.Instance);
Check(await noKey.ExtractAsync(AiDocumentKind.OwnerIdentity, "owner.pdf", bytes) == null && handler.Calls == 0, "missing key makes no HTTP call");
var configuredKey = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "test" }).Build();
var limited = new GeminiDocumentValidationService(new HttpClient(new StatusHandler(System.Net.HttpStatusCode.TooManyRequests, "{}")), configuredKey, NullLogger<GeminiDocumentValidationService>.Instance);
Check(await limited.ExtractAsync(AiDocumentKind.OwnerIdentity, "owner.pdf", bytes) == null, "HTTP 429 becomes UnableToVerify extraction failure");
var malformed = new GeminiDocumentValidationService(new HttpClient(new StatusHandler(System.Net.HttpStatusCode.OK, "not-json")), configuredKey, NullLogger<GeminiDocumentValidationService>.Instance);
Check(await malformed.ExtractAsync(AiDocumentKind.OwnerIdentity, "owner.pdf", bytes) == null, "malformed Gemini response becomes extraction failure");

var database = Path.Combine(Path.GetTempPath(), $"ai-document-validation-{Guid.NewGuid():N}.db");
try
{
    var services = new ServiceCollection(); services.AddLogging(); services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={database}"));
    services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>(); services.AddScoped<AiSettingsService>();
    await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.MigrateAsync();
    var initial = await db.AiSettings.SingleAsync();
    Check(!initial.DocumentValidationEnabled && !initial.CertificateOfIncorporationValidationEnabled && !initial.ProofOfAddressValidationEnabled && !initial.ApplicationSummariesEnabled, "all validation settings default independently OFF");
    Check(!db.Database.HasPendingModelChanges(), "SQLite migrations match model");
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(); foreach (var role in new[] { "DEDATAdmin", "BusinessOwner" }) Check((await roles.CreateAsync(new(role))).Succeeded, "create " + role);
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    async Task<ApplicationUser> User(string name, string role) { var u = new ApplicationUser { UserName = name + "@test.local", Email = name + "@test.local", FullName = name }; Check((await users.CreateAsync(u)).Succeeded && (await users.AddToRoleAsync(u, role)).Succeeded, "create " + name); return u; }
    var admin = await User("admin", "DEDATAdmin"); var ownerUser = await User("owner", "BusinessOwner");
    ClaimsPrincipal Principal(ApplicationUser u) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id), new Claim("AspNet.Identity.SecurityStamp", u.SecurityStamp!)], "test"));
    var settings = scope.ServiceProvider.GetRequiredService<AiSettingsService>();
    await settings.SetDocumentValidationEnabledAsync(Principal(admin), true); await settings.SetCertificateValidationEnabledAsync(Principal(admin), true); await settings.SetProofOfAddressValidationEnabledAsync(Principal(admin), false);
    var configured = await settings.GetDocumentValidationSettingsAsync(); Check(configured.OwnerId && configured.CertificateOfIncorporation && !configured.ProofOfAddress, "three toggles are independent");
    var denied = false; try { await settings.SetCertificateValidationEnabledAsync(Principal(ownerUser), false); } catch (UnauthorizedAccessException) { denied = true; } Check(denied, "non-admin cannot change toggle");
    Check(typeof(AdminAiSettings).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles == "DEDATAdmin", "settings page requires DEDATAdmin");
}
finally { try { File.Delete(database); } catch (IOException) { } }
Console.WriteLine($"AI document validation: {checks} checks passed. No real Gemini requests were made.");

sealed class FakeExtractor : IAiDocumentValidationService
{
    public int Calls; public AiDocumentExtraction? Next;
    public Task<AiDocumentExtraction?> ExtractAsync(AiDocumentKind kind, string fileName, byte[] fileBytes, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(Next); }
}
sealed class CountingHandler : HttpMessageHandler
{
    public int Calls; protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)); }
}
sealed class StatusHandler(System.Net.HttpStatusCode status, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
}
