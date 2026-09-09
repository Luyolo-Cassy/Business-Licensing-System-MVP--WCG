using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public class MunicipalMessageService(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public async Task SaveReviewAsync(ClaimsPrincipal principal, int applicationId, string status, string content)
    {
        var official = await users.GetUserAsync(principal);
        if (official == null || !await users.IsInRoleAsync(official, "MunicipalOfficial")
            || string.IsNullOrWhiteSpace(official.Municipality))
            throw new UnauthorizedAccessException();

        var application = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId
            && (a.Municipality == official.Municipality || a.Municipality == null))
            ?? throw new UnauthorizedAccessException();

        application.Status = status;
        if (!string.IsNullOrWhiteSpace(content))
        {
            db.MunicipalMessages.Add(new MunicipalMessage
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
            await db.SaveChangesAsync();
        }
        catch
        {
            foreach (var entry in db.ChangeTracker.Entries<MunicipalMessage>()
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
