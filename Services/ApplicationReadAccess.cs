using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

// Shared authorization for generated PDFs and supporting/legacy uploaded documents.
public static class ApplicationReadAccess
{
    public static async Task<bool> CanReadAsync(ApplicationDbContext db, UserManager<ApplicationUser> users,
        ClaimsPrincipal principal, Application application)
    {
        if (principal.Identity?.IsAuthenticated != true) return false;
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
        if (user == null || (user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow)) return false;
        var stamp = principal.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (stamp != null && stamp != user.SecurityStamp) return false;
        if (await users.IsInRoleAsync(user, "DEDATAdmin")) return true;
        if (application.UserId == user.Id && await users.IsInRoleAsync(user, "BusinessOwner")) return true;
        var official = await OfficialAccess.GetAsync(db, principal);
        return official != null && official.Municipality == application.Municipality;
    }
}
