using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessLicensingSystem.Tests.MunicipalOfficialApplicationReview;

[Trait("Feature", "Municipal Official Application Review")]
public sealed class MunicipalApplicationReviewTests
{
    [Fact]
    public async Task SameMunicipalityOfficial_CanMoveApplicationToUnderReview()
    {
        await using var fixture = await ReviewFixture.CreateAsync();

        await fixture.Messages.SaveReviewAsync(fixture.Principal(fixture.Official), fixture.Application.Id,
            ApplicationWorkflow.UnderReview, "");

        var saved = await fixture.Db.Applications.AsNoTracking().SingleAsync(item => item.Id == fixture.Application.Id);
        Assert.Equal(ApplicationWorkflow.UnderReview, saved.Status);
        Assert.Single(fixture.Notifications.Reviews);
    }

    [Fact]
    public async Task DifferentMunicipalityOfficial_CannotReviewApplication()
    {
        await using var fixture = await ReviewFixture.CreateAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Messages.SaveReviewAsync(
            fixture.Principal(fixture.OtherOfficial), fixture.Application.Id, ApplicationWorkflow.Rejected, "Forbidden"));

        var saved = await fixture.Db.Applications.AsNoTracking().SingleAsync(item => item.Id == fixture.Application.Id);
        Assert.Equal(ApplicationWorkflow.Submitted, saved.Status);
        Assert.Empty(await fixture.Db.MunicipalMessages.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Notifications.Reviews);
    }

    [Fact]
    public async Task StatusChangeWithMessage_PersistsBothAtomically()
    {
        await using var fixture = await ReviewFixture.CreateAsync();
        const string message = "Please provide a clearer proof of address.";

        await fixture.Messages.SaveReviewAsync(fixture.Principal(fixture.Official), fixture.Application.Id,
            ApplicationWorkflow.UnderReview, message);

        var saved = await fixture.Db.Applications.AsNoTracking().SingleAsync(item => item.Id == fixture.Application.Id);
        var persistedMessage = await fixture.Db.MunicipalMessages.AsNoTracking().SingleAsync();
        Assert.Equal(ApplicationWorkflow.UnderReview, saved.Status);
        Assert.Equal(message, persistedMessage.Content);
        Assert.Equal(fixture.Official.Id, persistedMessage.SenderId);
        Assert.Null(persistedMessage.ReadAtUtc);
    }

    [Fact]
    public async Task RejectedApplication_RecordsDecisionDateAndReason()
    {
        await using var fixture = await ReviewFixture.CreateAsync();
        const string reason = "Required municipal documents were not supplied.";

        await fixture.Messages.SaveReviewAsync(fixture.Principal(fixture.Official), fixture.Application.Id,
            ApplicationWorkflow.Rejected, reason);

        var saved = await fixture.Db.Applications.AsNoTracking().SingleAsync(item => item.Id == fixture.Application.Id);
        Assert.Equal(ApplicationWorkflow.Rejected, saved.Status);
        Assert.Equal(reason, saved.DecisionReason);
        Assert.NotNull(saved.DecisionDateUtc);
        Assert.Contains(await fixture.Db.ApplicationAuditLogs.AsNoTracking().ToListAsync(),
            item => item.ApplicationId == saved.Id && item.EventType == ApplicationAuditEventTypes.ApplicationRejected);
    }

    [Fact]
    public async Task Applicant_CanReadOnlyMessagesForOwnedApplication()
    {
        await using var fixture = await ReviewFixture.CreateAsync();
        await fixture.Messages.SaveReviewAsync(fixture.Principal(fixture.Official), fixture.Application.Id,
            ApplicationWorkflow.UnderReview, "Message for the first applicant");
        await fixture.Messages.SaveReviewAsync(fixture.Principal(fixture.OtherOfficial), fixture.OtherApplication.Id,
            ApplicationWorkflow.UnderReview, "Message for the other applicant");

        var ownMessages = await fixture.Messages.GetMessagesAsync(fixture.Principal(fixture.Owner), fixture.Application.Id);
        var foreignMessages = await fixture.Messages.GetMessagesAsync(fixture.Principal(fixture.Owner), fixture.OtherApplication.Id);

        Assert.Single(ownMessages);
        Assert.Equal("Message for the first applicant", ownMessages[0].Content);
        Assert.Empty(foreignMessages);
    }

    private sealed class ReviewFixture : IAsyncDisposable
    {
        private readonly string databasePath;
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;

        public ApplicationDbContext Db { get; }
        public MunicipalMessageService Messages { get; }
        public RecordingNotificationService Notifications { get; }
        public ApplicationUser Owner { get; private set; } = null!;
        public ApplicationUser OtherOwner { get; private set; } = null!;
        public ApplicationUser Official { get; private set; } = null!;
        public ApplicationUser OtherOfficial { get; private set; } = null!;
        public Application Application { get; private set; } = null!;
        public Application OtherApplication { get; private set; } = null!;

        private ReviewFixture(string databasePath, ServiceProvider provider, AsyncServiceScope scope,
            ApplicationDbContext db, MunicipalMessageService messages, RecordingNotificationService notifications)
        {
            this.databasePath = databasePath;
            this.provider = provider;
            this.scope = scope;
            Db = db;
            Messages = messages;
            Notifications = notifications;
        }

        public static async Task<ReviewFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"municipal-review-xunit-{Guid.NewGuid():N}.db");
            var notifications = new RecordingNotificationService();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            services.AddSingleton<IApplicationNotificationService>(notifications);
            services.AddScoped<MunicipalMessageService>();
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            var fixture = new ReviewFixture(databasePath, provider, scope, db,
                scope.ServiceProvider.GetRequiredService<MunicipalMessageService>(), notifications);
            await fixture.SeedAsync();
            return fixture;
        }

        public ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!)], "test"));

        private async Task SeedAsync()
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in new[] { "BusinessOwner", "MunicipalOfficial" })
                Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);

            Owner = await CreateUserAsync(users, "owner@example.test", "Owner", "BusinessOwner");
            OtherOwner = await CreateUserAsync(users, "other-owner@example.test", "Other Owner", "BusinessOwner");
            Official = await CreateUserAsync(users, "official@example.test", "Bergrivier Official", "MunicipalOfficial", "Bergrivier Municipality");
            OtherOfficial = await CreateUserAsync(users, "other-official@example.test", "Swartland Official", "MunicipalOfficial", "Swartland Municipality");

            Application = new Application
            {
                UserId = Owner.Id, Municipality = "Bergrivier Municipality", ApplicationNumber = "REVIEW-001",
                LicenceType = "Sale of Meals Licence", BusinessName = "Owner Business", Status = ApplicationWorkflow.Submitted,
                DateSubmitted = DateTime.UtcNow
            };
            OtherApplication = new Application
            {
                UserId = OtherOwner.Id, Municipality = "Swartland Municipality", ApplicationNumber = "REVIEW-002",
                LicenceType = "Sale of Meals Licence", BusinessName = "Other Business", Status = ApplicationWorkflow.Submitted,
                DateSubmitted = DateTime.UtcNow
            };
            Db.Applications.AddRange(Application, OtherApplication);
            await Db.SaveChangesAsync();
        }

        private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users, string email,
            string fullName, string role, string? municipality = null)
        {
            var user = new ApplicationUser { UserName = email, Email = email, FullName = fullName, Municipality = municipality };
            Assert.True((await users.CreateAsync(user, "Password123!")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try { File.Delete(databasePath); } catch (IOException) { }
        }
    }

    private sealed class RecordingNotificationService : IApplicationNotificationService
    {
        public List<(int ApplicationId, string PreviousStatus, string NewStatus, string? Message)> Reviews { get; } = [];
        public Task NotifySubmissionAsync(int applicationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyReviewAsync(int applicationId, string previousStatus, string newStatus, string? municipalMessage,
            CancellationToken cancellationToken = default)
        {
            Reviews.Add((applicationId, previousStatus, newStatus, municipalMessage));
            return Task.CompletedTask;
        }
        public Task NotifyResubmissionAsync(int applicationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
