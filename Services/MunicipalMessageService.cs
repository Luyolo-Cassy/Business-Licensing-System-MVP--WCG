using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public class MunicipalMessageService(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IServiceScopeFactory scopes, IApplicationNotificationService? notifications = null,
    ILogger<MunicipalMessageService>? logger = null)
{
    public async Task SaveReviewAsync(ClaimsPrincipal principal, int applicationId, string status, string content)
    {
        await using var scope = scopes.CreateAsyncScope();
        var reviewDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await reviewDb.Database.BeginTransactionAsync();
        var official = await OfficialAccess.GetAsync(reviewDb, principal);
        if (official == null)
            throw new UnauthorizedAccessException();

        var application = await reviewDb.Applications.FirstOrDefaultAsync(a => a.Id == applicationId
            && a.Municipality == official.Municipality)
            ?? throw new UnauthorizedAccessException();

        var previousStatus = application.Status;
        application.Status = status;
        if (status is "Licence Issued" or "Rejected" &&
            (previousStatus != status || application.DecisionDateUtc == null))
        {
            application.DecisionDateUtc = DateTime.UtcNow;
            if (status == "Rejected" && !string.IsNullOrWhiteSpace(content))
                application.DecisionReason = content.Trim();
        }
        var newMessage = string.IsNullOrWhiteSpace(content) ? null : content;
        if (newMessage != null)
        {
            reviewDb.MunicipalMessages.Add(new MunicipalMessage
            {
                ApplicationId = application.Id,
                Content = newMessage,
                SenderId = official.Id,
                SenderName = official.FullName,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        var statusChanged = !string.Equals(previousStatus, application.Status, StringComparison.Ordinal);
        if (statusChanged && application.Status == ApplicationWorkflow.LicenceIssued)
        {
            ApplicationAuditService.Add(reviewDb, application, official, "MunicipalOfficial",
                ApplicationAuditEventTypes.ApplicationApproved, "Application approved and license issued.",
                previousStatus, application.Status, newMessage == null ? null :
                new Dictionary<string, object?> { ["message"] = newMessage });
        }
        else if (statusChanged && application.Status == ApplicationWorkflow.Rejected)
        {
            ApplicationAuditService.Add(reviewDb, application, official, "MunicipalOfficial",
                ApplicationAuditEventTypes.ApplicationRejected, "Application rejected.",
                previousStatus, application.Status, newMessage == null ? null :
                new Dictionary<string, object?> { ["decisionReason"] = application.DecisionReason, ["message"] = newMessage });
        }
        else
        {
            if (statusChanged)
                ApplicationAuditService.Add(reviewDb, application, official, "MunicipalOfficial",
                    ApplicationAuditEventTypes.StatusChanged, $"Status changed from {previousStatus} to {application.Status}.",
                    previousStatus, application.Status);
            if (newMessage != null)
                ApplicationAuditService.Add(reviewDb, application, official, "MunicipalOfficial",
                    ApplicationAuditEventTypes.ReviewMessageSent, "Review message sent to applicant.",
                    metadata: new Dictionary<string, object?> { ["message"] = newMessage });
        }
        // Status and the message/notification commit atomically.
        try
        {
            await reviewDb.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            foreach (var entry in reviewDb.ChangeTracker.Entries<MunicipalMessage>()
                         .Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            throw;
        }
        if (notifications != null && (statusChanged || newMessage != null))
        {
            try { await notifications.NotifyReviewAsync(application.Id, previousStatus, application.Status, newMessage); }
            catch (Exception error)
            {
                logger?.LogError(error,
                    "Post-commit application notification failed. ApplicationId={ApplicationId} PreviousStatus={PreviousStatus} NewStatus={NewStatus} Outcome=Failed ExceptionType={ExceptionType}",
                    application.Id, previousStatus, application.Status, error.GetType().Name);
            }
        }
    }

    public async Task<List<MunicipalMessage>> GetMessagesAsync(ClaimsPrincipal principal, int applicationId)
    {
        var user = await users.GetUserAsync(principal);
        if (user == null || !await users.IsInRoleAsync(user, "BusinessOwner")) return [];
        return await db.MunicipalMessages.AsNoTracking()
            .Where(m => m.ApplicationId == applicationId && m.Application.UserId == user.Id)
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).ToListAsync();
    }

    public async Task MarkReadAsync(ClaimsPrincipal principal, int applicationId, int[] displayedIds)
    {
        var user = await users.GetUserAsync(principal);
        if (user == null || !await users.IsInRoleAsync(user, "BusinessOwner")) return;
        // Only messages actually displayed are read; concurrently arriving messages stay unread.
        await db.MunicipalMessages.Where(m => m.ApplicationId == applicationId
            && m.Application.UserId == user.Id && displayedIds.Contains(m.Id) && m.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAtUtc, DateTime.UtcNow));
    }

    public async Task<Dictionary<int, int>> GetUnreadCountsAsync(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user == null || !await users.IsInRoleAsync(user, "BusinessOwner")) return [];
        return await db.MunicipalMessages.AsNoTracking()
            .Where(m => m.Application.UserId == user.Id && m.ReadAtUtc == null)
            .GroupBy(m => m.ApplicationId)
            .Select(g => new { ApplicationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ApplicationId, g => g.Count);
    }
}
