using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using BusinessLicensing_Practice.Services.Storage;
using BusinessLicensingSystem.Tests.PdfGenerationAndStorage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BusinessLicensingSystem.Tests.ApplicantCorrectionWorkflow;

[Collection("PDF workflows")]
[Trait("Feature", "Applicant Correction and Resubmission")]
public sealed class ApplicantCorrectionWorkflowTests
{
    [Fact]
    public async Task Official_CanRequestCorrectionsAndCreateOpenRequest()
    {
        await using var fixture = await CorrectionFixture.CreateAsync();

        await fixture.Corrections.RequestCorrectionsAsync(fixture.Principal(fixture.Official), fixture.Application.Id,
            "Replace the proof of address.");

        var application = await fixture.Db.Applications.AsNoTracking().SingleAsync(item => item.Id == fixture.Application.Id);
        var request = await fixture.Db.MunicipalMessages.AsNoTracking().SingleAsync();
        Assert.Equal(ApplicationWorkflow.AdditionalInformationRequired, application.Status);
        Assert.Equal(ApplicationWorkflow.CorrectionRequestMessage, request.MessageType);
        Assert.Equal("Replace the proof of address.", request.Content);
        Assert.Null(request.ResolvedAtUtc);
    }

    [Fact]
    public async Task DifferentMunicipalityOfficial_CannotRequestCorrections()
    {
        await using var fixture = await CorrectionFixture.CreateAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Corrections.RequestCorrectionsAsync(
            fixture.Principal(fixture.OtherOfficial), fixture.Application.Id, "Forbidden correction request"));

        var application = await fixture.Db.Applications.AsNoTracking().SingleAsync(item => item.Id == fixture.Application.Id);
        Assert.Equal(ApplicationWorkflow.Submitted, application.Status);
        Assert.Empty(await fixture.Db.MunicipalMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CorrectionDraft_PreservesExistingFieldsAndDocumentInformation()
    {
        await using var fixture = await CorrectionFixture.CreateAsync();
        await fixture.OpenCorrectionAsync();

        var loaded = await fixture.Corrections.LoadOrCreateDraftAsync(fixture.Principal(fixture.Owner), fixture.Application.Id);
        var payload = ApplicationDraftService.Deserialize(loaded.Draft.PayloadJson);

        Assert.Equal(fixture.Application.Id, loaded.Draft.SourceApplicationId);
        Assert.Equal("Audit Cafe", payload.BusinessName);
        Assert.Equal("2026/123456/07", payload.RegistrationNumber);
        Assert.Equal("12 Main Street", payload.PlaceOfBusinessAddressLine1);
        Assert.Equal(fixture.Application.Documents.Count, loaded.Draft.Documents.Count);
        Assert.Equal(fixture.Application.Documents.Select(item => item.DocumentType).Order(),
            loaded.Draft.Documents.Select(item => item.DocumentType).Order());
        Assert.All(loaded.Draft.Documents, item => Assert.EndsWith(".pdf", item.FileName));
    }

    [Fact]
    public async Task Applicant_CannotChangeLicenceTypeDuringCorrection()
    {
        await using var fixture = await CorrectionFixture.CreateAsync();
        await fixture.OpenCorrectionAsync();
        var loaded = await fixture.Corrections.LoadOrCreateDraftAsync(fixture.Principal(fixture.Owner), fixture.Application.Id);
        var payload = ApplicationDraftService.Deserialize(loaded.Draft.PayloadJson);
        payload.LicenceType = "Health Facility Licence";

        var error = await Assert.ThrowsAsync<ValidationException>(() => fixture.Corrections.SaveDraftAsync(
            fixture.Principal(fixture.Owner), fixture.Application.Id, payload, 4));

        Assert.Contains("license type cannot be changed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resubmission_PreservesIdentityIncrementsRevisionResolvesRequestAndGeneratesPdf()
    {
        PdfTestSupport.EnsureFontResolver();
        await using var fixture = await CorrectionFixture.CreateAsync();
        var originalId = fixture.Application.Id;
        var originalReference = fixture.Application.ApplicationNumber;
        await fixture.OpenCorrectionAsync();
        var loaded = await fixture.Corrections.LoadOrCreateDraftAsync(fixture.Principal(fixture.Owner), originalId);
        var payload = ApplicationDraftService.Deserialize(loaded.Draft.PayloadJson);
        payload.BusinessName = "Corrected Audit Cafe";
        payload.DeclarationAccepted = true;
        payload.PopiaConsentAccepted = true;
        await fixture.Corrections.SaveDraftAsync(fixture.Principal(fixture.Owner), originalId, payload, 7);

        await fixture.Corrections.ResubmitAsync(fixture.Principal(fixture.Owner), originalId);

        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Applications.AsNoTracking().SingleAsync(item => item.Id == originalId);
        var request = await fixture.Db.MunicipalMessages.AsNoTracking().SingleAsync(item => item.ApplicationId == originalId);
        Assert.Equal(originalId, saved.Id);
        Assert.Equal(originalReference, saved.ApplicationNumber);
        Assert.Equal(2, saved.RevisionNumber);
        Assert.Equal(ApplicationWorkflow.Submitted, saved.Status);
        Assert.Equal("Corrected Audit Cafe", saved.BusinessName);
        Assert.NotNull(saved.LastResubmittedAtUtc);
        Assert.NotNull(request.ResolvedAtUtc);
        Assert.False(await fixture.Db.ApplicationDrafts.AnyAsync(item => item.SourceApplicationId == originalId));
        Assert.Equal($"{originalReference}-Official-Application-r2.pdf", saved.ApplicationFormFileName);
        Assert.True(await fixture.Files.GeneratedPdfExistsAsync(saved.ApplicationFormFilePath));
    }

    private sealed class CorrectionFixture : IAsyncDisposable
    {
        private readonly string root;
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;
        public ApplicationDbContext Db { get; }
        public ApplicationCorrectionService Corrections { get; }
        public ApplicationFileService Files { get; }
        public ApplicationUser Owner { get; private set; } = null!;
        public ApplicationUser Official { get; private set; } = null!;
        public ApplicationUser OtherOfficial { get; private set; } = null!;
        public Application Application { get; private set; } = null!;

        private CorrectionFixture(string root, ServiceProvider provider, AsyncServiceScope scope,
            ApplicationDbContext db, ApplicationCorrectionService corrections, ApplicationFileService files)
        {
            this.root = root; this.provider = provider; this.scope = scope;
            Db = db; Corrections = corrections; Files = files;
        }

        public static async Task<CorrectionFixture> CreateAsync()
        {
            PdfTestSupport.EnsureFontResolver();
            var root = Path.Combine(Path.GetTempPath(), $"correction-xunit-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(root, "correction.db")}"));
            services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
            services.AddSingleton<IApplicationNotificationService, NoNotifications>();
            services.AddSingleton<IWebHostEnvironment>(new TestEnvironment(root));
            services.AddSingleton<IPrivateFileStore, LocalPrivateFileStore>();
            services.AddSingleton<ApplicationFileService>();
            services.AddSingleton<ProtectedUploadService>();
            services.AddSingleton<IAiDocumentValidationService, NoDocumentExtraction>();
            services.AddScoped<AiDocumentValidationPolicy>();
            services.AddScoped<AiSettingsService>();
            services.AddScoped<AiDocumentValidationWorkflow>();
            services.AddSingleton<ApplicationPdfService>();
            services.AddSingleton(new ArcGisGeocodingService(new HttpClient(new JsonHandler(
                "{\"candidates\":[{\"address\":\"12 Main Street\",\"attributes\":{\"Addr_type\":\"PointAddress\"},\"location\":{\"x\":18.1,\"y\":-33.1}}]}")),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ArcGIS:ApiKey"] = "offline-test-key" }).Build()));
            services.AddSingleton(new WcgMunicipalBoundaryService(new HttpClient(new JsonHandler(
                "{\"features\":[{\"attributes\":{\"AFRIGIS_LocalMunicipalities.S12_NAME\":\"Bergrivier Local Municipality\",\"AFRIGIS_LocalMunicipalities.MUN_CODE\":\"WC013\"}}]}"))));
            services.AddSingleton<MunicipalRoutingService>();
            services.AddScoped<ApplicationCorrectionService>();
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            var fixture = new CorrectionFixture(root, provider, scope, db,
                scope.ServiceProvider.GetRequiredService<ApplicationCorrectionService>(),
                scope.ServiceProvider.GetRequiredService<ApplicationFileService>());
            await fixture.SeedAsync();
            return fixture;
        }

        public ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!)], "test"));

        public Task OpenCorrectionAsync() => Corrections.RequestCorrectionsAsync(Principal(Official), Application.Id,
            "Replace the proof of address.");

        private async Task SeedAsync()
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in new[] { "BusinessOwner", "MunicipalOfficial" })
                Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
            Owner = await User(users, "applicant@example.test", "Applicant One", "BusinessOwner");
            Official = await User(users, "official@example.test", "Bergrivier Official", "MunicipalOfficial", "Bergrivier Municipality");
            OtherOfficial = await User(users, "other-official@example.test", "Swartland Official", "MunicipalOfficial", "Swartland Municipality");

            var details = new ApplicationDetails
            {
                ApplicationType = "New application", ApplicantFirstName = "Applicant", ApplicantLastName = "One",
                ApplicantAddressLine1 = "1 Home Road", ApplicantSuburb = "Central", ApplicantCity = "Piketberg",
                ApplicantPostalCode = "7320", ApplicantTelephone = "0211234567", ApplicantEmail = "applicant@example.test",
                PostalAddressSameAsBusiness = true, ContactPerson = "Applicant One", BusinessTelephone = "0211234567",
                BusinessEmail = "business@example.test",
                TradingHours = ApplicationEntry.SerializeTradingHours(ApplicationEntry.Days.Select(day => new TradingDay { Day = day, IsOpen = false }).ToList()),
                OpenOnPublicHolidays = false,
                LicenceSpecificDetailsJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["foodDescription"] = "Meals", ["foodHandling"] = "Pre-packed", ["tradingInformation"] = "Counter service"
                }),
                DeclarationAccepted = true, DeclarationAcceptedAt = DateTime.UtcNow
            };
            Application = new Application
            {
                ApplicationNumber = "COR-2026-001", UserId = Owner.Id, Municipality = "Bergrivier Municipality",
                Status = ApplicationWorkflow.Submitted, RevisionNumber = 1, LicenceType = "Sale of Meals Licence",
                BusinessName = "Audit Cafe", RegistrationNumber = "2026/123456/07", TaxNumber = "1234567890",
                BusinessCategory = "Food", PlaceOfBusinessAddress = "12 Main Street, Central, Piketberg, 7320",
                PlaceOfBusinessAddressLine1 = "12 Main Street", PlaceOfBusinessSuburb = "Central",
                PlaceOfBusinessCity = "Piketberg", PlaceOfBusinessPostalCode = "7320", PopiaConsentAccepted = true,
                DateSubmitted = DateTime.UtcNow, Details = details,
                Documents = LicenceApplicationCatalog.Find("Sale of Meals Licence")!.Documents.Select(document =>
                    new ApplicationDocument { DocumentType = document.DocumentType, FileName = document.DocumentType + ".pdf", FilePath = "/uploads/fixture.pdf" }).ToList()
            };
            Db.Applications.Add(Application);
            await Db.SaveChangesAsync();
        }

        private static async Task<ApplicationUser> User(UserManager<ApplicationUser> users, string email, string name,
            string role, string? municipality = null)
        {
            var user = new ApplicationUser { UserName = email, Email = email, FullName = name, Municipality = municipality };
            Assert.True((await users.CreateAsync(user, "Password123!")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private sealed class NoNotifications : IApplicationNotificationService
    {
        public Task NotifySubmissionAsync(int applicationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyReviewAsync(int applicationId, string previousStatus, string newStatus, string? municipalMessage, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyResubmissionAsync(int applicationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoDocumentExtraction : IAiDocumentValidationService
    {
        public Task<AiDocumentExtraction?> ExtractAsync(AiDocumentKind kind, string fileName, byte[] fileBytes,
            CancellationToken cancellationToken = default) => Task.FromResult<AiDocumentExtraction?>(null);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "BusinessLicensingSystem.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }
}
