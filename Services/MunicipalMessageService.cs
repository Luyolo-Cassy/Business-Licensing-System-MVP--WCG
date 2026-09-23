using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public class MunicipalMessageService(ApplicationDbContext db, UserManager<ApplicationUser> users, IServiceScopeFactory scopes)
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
        if (!string.IsNullOrWhiteSpace(content))
        {
            reviewDb.MunicipalMessages.Add(new MunicipalMessage
            {
                ApplicationId = application.Id,
                Content = content,
                SenderId = official.Id,
                SenderName = official.FullName,
                CreatedAtUtc = DateTime.UtcNow
            });
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
