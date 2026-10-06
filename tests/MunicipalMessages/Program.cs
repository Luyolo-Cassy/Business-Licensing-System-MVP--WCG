using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using BusinessLicensing_Practice.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

var database = Path.Combine(Path.GetTempPath(), $"municipal-messages-{Guid.NewGuid():N}.db");
var notifications = new RecordingNotificationService();
ServiceProvider CreateProvider()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={database}"));
    services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<ApplicationDbContext>();
    services.AddSingleton<IApplicationNotificationService>(notifications);
    services.AddScoped<MunicipalMessageService>();
    return services.BuildServiceProvider();
}
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine($"PASS: {description}");
}
ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity(
    [new Claim(ClaimTypes.NameIdentifier, id)], "test"));
async Task Denied(Func<Task> action)
{
    try { await action(); }
    catch (UnauthorizedAccessException) { return; }
    throw new Exception("Expected authorization rejection");
}

int applicationId, otherApplicationId;
string ownerId, otherOwnerId, officialId, wrongOfficialId;
var longMessage = "Please supply the following:\n" + new string('x', 20000) + "\n<script>alert('test')</script>";
await using (var provider = CreateProvider())
{
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "BusinessOwner", "MunicipalOfficial" })
        Check((await roles.CreateAsync(new IdentityRole(role))).Succeeded, $"Create {role} role");
    async Task<string> User(string name, string role, string? municipality = null)
    {
        var email = $"{name}@example.test";
        var user = new ApplicationUser { UserName = email, Email = email, FullName = name, Municipality = municipality };
        Check((await users.CreateAsync(user, "Password123!")).Succeeded, $"Create {name}");
        await users.AddToRoleAsync(user, role);
        Check(await users.CheckPasswordAsync(user, "Password123!"), $"Authenticate {name}");
        return user.Id;
    }
    ownerId = await User("owner", "BusinessOwner");
    otherOwnerId = await User("other-owner", "BusinessOwner");
    officialId = await User("official", "MunicipalOfficial", "City of Cape Town");
    wrongOfficialId = await User("other-official", "MunicipalOfficial", "Other municipality");
    var application = new Application { UserId = ownerId, Municipality = "City of Cape Town", ApplicationNumber = "TEST-1", LicenceType = "Sale of Meals Licence", Status = "Submitted",
        Details = new ApplicationDetails { ApplicantFirstName = "Owner", ApplicantLastName = "Example", ApplicantEmail = "applicant@example.test" } };
    var other = new Application { UserId = otherOwnerId, Municipality = "Other municipality", ApplicationNumber = "TEST-2", Status = "Submitted" };
    db.Applications.AddRange(application, other);
    await db.SaveChangesAsync();
    applicationId = application.Id;
    otherApplicationId = other.Id;
    Check(!db.Database.HasPendingModelChanges(), "Current model matches migrations; no migration required");
    Check(await db.Applications.CountAsync() == 2, "Migration preserves existing applications");
    var service = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
    await service.SaveReviewAsync(Principal(officialId), applicationId, "Under Review", longMessage);
    Check(notifications.Reviews.Count == 1 && notifications.Reviews[0] == (applicationId, "Submitted", "Under Review", longMessage), "Status change plus message requests exactly one combined notification");
    var saved = await db.MunicipalMessages.SingleAsync();
    Check(saved.Content == longMessage && saved.SenderId == officialId && saved.CreatedAtUtc != default && saved.ReadAtUtc == null, "Full text, sender, timestamp and unread notification saved");
    await service.SaveReviewAsync(Principal(officialId), applicationId, "Under Review", "   ");
    Check(await db.MunicipalMessages.CountAsync() == 1, "Status-only review does not create empty notification");
    Check(notifications.Reviews.Count == 1, "Same status with no message requests no notification");
    await Denied(() => service.SaveReviewAsync(Principal(wrongOfficialId), applicationId, "Rejected", "Forbidden"));
    await Denied(() => service.SaveReviewAsync(Principal(ownerId), applicationId, "Rejected", "Forbidden"));
    Check(await db.MunicipalMessages.CountAsync() == 1, "Municipality and role restrictions enforced");
    Check(notifications.Reviews.Count == 1, "Unauthorized and cross-municipality reviews request no notification");
    var unrouted = new Application { UserId = ownerId, Municipality = null, Status = "Submitted" };
    db.Applications.Add(unrouted);
    await db.SaveChangesAsync();
    await Denied(() => service.SaveReviewAsync(Principal(officialId), unrouted.Id, "Rejected", "Forbidden"));
    Check(unrouted.Status == "Submitted", "Official cannot review an unrouted legacy application");
}
// Dispose the entire provider and reopen the file database to simulate process restart.
await using (var provider = CreateProvider())
{
    await using var scope = provider.CreateAsyncScope();
    var service = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Check((await service.GetUnreadCountsAsync(Principal(ownerId)))[applicationId] == 1, "Unread notification survives restart and identifies application");
    Check((await service.GetMessagesAsync(Principal(otherOwnerId), applicationId)).Count == 0, "Another applicant cannot view messages");
    Check((await service.GetUnreadCountsAsync(Principal(otherOwnerId))).Count == 0, "Another applicant cannot see notification counts");
    var displayed = await service.GetMessagesAsync(Principal(ownerId), applicationId);
    Check(displayed.Single().Content == longMessage, "Message text survives restart");
    await service.MarkReadAsync(Principal(otherOwnerId), applicationId, [displayed[0].Id]);
    Check((await service.GetUnreadCountsAsync(Principal(ownerId)))[applicationId] == 1, "Another applicant cannot mark messages read");
    await service.SaveReviewAsync(Principal(officialId), applicationId, "Under Review", "Follow-up");
    Check(notifications.Reviews.Count == 2 && notifications.Reviews[^1].Message == "Follow-up", "Same status plus message requests exactly one message notification");
    await service.MarkReadAsync(Principal(ownerId), applicationId, displayed.Select(m => m.Id).ToArray());
    Check((await service.GetUnreadCountsAsync(Principal(ownerId)))[applicationId] == 1, "Reading displayed messages leaves concurrent new message unread");
    var all = await service.GetMessagesAsync(Principal(ownerId), applicationId);
    Check(all.Count == 2 && all[0].Content == longMessage && all[1].Content == "Follow-up", "Messages appear in chronological order");
    await service.MarkReadAsync(Principal(ownerId), applicationId, all.Select(m => m.Id).ToArray());
    Check((await service.GetUnreadCountsAsync(Principal(ownerId))).Count == 0, "Viewing all messages clears unread count");

    await service.SaveReviewAsync(Principal(officialId), applicationId, "Department Assessment", "");
    Check(notifications.Reviews.Count == 3 && notifications.Reviews[^1].PreviousStatus == "Under Review" && notifications.Reviews[^1].NewStatus == "Department Assessment" && notifications.Reviews[^1].Message == null,
        "Actual status change requests exactly one status notification");
    await service.SaveReviewAsync(Principal(officialId), applicationId, "Final Decision", "Assessment complete");
    Check(notifications.Reviews.Count == 4 && notifications.Reviews[^1].Message == "Assessment complete", "Status change and message remain one notification request");
    await service.SaveReviewAsync(Principal(officialId), applicationId, "Licence Issued", "");
    Check(notifications.Reviews[^1].NewStatus == "Licence Issued", "Approve status requests a Licence Issued notification");
    await service.SaveReviewAsync(Principal(officialId), applicationId, "Rejected", "Requirements were not met");
    var rejected = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId);
    Check(notifications.Reviews[^1].NewStatus == "Rejected" && rejected.DecisionReason == "Requirements were not met" && rejected.DecisionDateUtc != null,
        "Reject status requests a Rejected notification and persists decision information");

    notifications.Throw = true;
    await service.SaveReviewAsync(Principal(officialId), applicationId, "Under Review", "Email transport failure message");
    db.ChangeTracker.Clear();
    Check((await db.Applications.SingleAsync(a => a.Id == applicationId)).Status == "Under Review", "Notification failure does not undo committed application status");
    Check(await db.MunicipalMessages.AnyAsync(m => m.ApplicationId == applicationId && m.Content == "Email transport failure message"), "Notification failure does not undo a Municipal Message");
    notifications.Throw = false;
    var remaining = await service.GetMessagesAsync(Principal(ownerId), applicationId);
    await service.MarkReadAsync(Principal(ownerId), applicationId, remaining.Select(message => message.Id).ToArray());
}
await using (var provider = CreateProvider())
{
    await using var scope = provider.CreateAsyncScope();
    var service = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Check((await service.GetUnreadCountsAsync(Principal(ownerId))).Count == 0, "Read state survives restart");
    Check((await service.GetMessagesAsync(Principal(ownerId), applicationId)).Count == 5, "Read messages remain available after restart");

    var email = new RecordingEmailService();
    var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ApplicationNotificationService>>();
    var applicationNotifications = new ApplicationNotificationService(
        scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), email, logger);
    var application = await db.Applications.Include(a => a.Details).SingleAsync(a => a.Id == applicationId);
    application.Details!.ApplicantEmail = "not-an-email";
    await db.SaveChangesAsync();
    await applicationNotifications.NotifySubmissionAsync(applicationId);
    Check(email.Messages.Count == 1 && email.Messages[0].RecipientAddress == "owner@example.test" &&
        email.Messages[0].Subject == "Application Submitted Successfully – TEST-1", "Submission notification uses valid account-email fallback and expected subject");
    Check(email.Messages[0].PlainTextBody.Contains("Status: Submitted") && email.Messages[0].PlainTextBody.Contains("Sale of Meals License"),
        "Submission notification contains application details");
    Check(email.Messages[0].PlainTextBody.Contains("Current stage: Application Submitted") &&
        email.Messages[0].PlainTextBody.Contains("Next: The municipality will begin reviewing the application."),
        "Applicant email includes centralized current-stage context and next step");
    var correctionContext = ApplicationStatusContext.For(ApplicationWorkflow.AdditionalInformationRequired);
    Check(correctionContext.Title == "Applicant Action Required" && correctionContext.Next!.Contains("resubmit"),
        "Additional-information status supplies applicant action context");

    var owner = await db.Users.SingleAsync(u => u.Id == ownerId);
    owner.Email = null;
    application.Details.ApplicantEmail = "still-invalid";
    await db.SaveChangesAsync();
    await applicationNotifications.NotifyReviewAsync(applicationId, "Submitted", "Under Review", null);
    Check(email.Messages.Count == 1, "No valid recipient skips notification without failing the operation");

    owner.Email = "owner@example.test";
    application.Details.ApplicantEmail = "applicant@example.test";
    await db.SaveChangesAsync();
    email.Throw = true;
    await applicationNotifications.NotifySubmissionAsync(applicationId);
    Check(email.Attempts == 2, "Submission notification contains a simulated transport failure");
    Check(scope.ServiceProvider.GetService<IEmailSender<ApplicationUser>>() == null, "Application notifications do not use Identity IEmailSender");
}
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
File.Delete(database);
Console.WriteLine("All municipal message integration checks passed.");

sealed class RecordingNotificationService : IApplicationNotificationService
{
    public List<(int ApplicationId, string PreviousStatus, string NewStatus, string? Message)> Reviews { get; } = [];
    public bool Throw { get; set; }
    public Task NotifySubmissionAsync(int applicationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task NotifyReviewAsync(int applicationId, string previousStatus, string newStatus, string? municipalMessage, CancellationToken cancellationToken = default)
    {
        Reviews.Add((applicationId, previousStatus, newStatus, municipalMessage));
        return Throw ? Task.FromException(new InvalidOperationException("Simulated notification failure")) : Task.CompletedTask;
    }
    public Task NotifyResubmissionAsync(int applicationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class RecordingEmailService : IEmailService
{
    public List<EmailMessage> Messages { get; } = [];
    public int Attempts { get; private set; }
    public bool Throw { get; set; }
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Attempts++;
        if (Throw) throw new InvalidOperationException("Simulated SMTP failure");
        Messages.Add(message);
        return Task.CompletedTask;
    }
}
