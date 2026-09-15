using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public static class OfficialAccess
{
    // Always read database values rather than a circuit's tracked Identity user.
    public static async Task<ApplicationUser?> GetAsync(ApplicationDbContext db, ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
        if (user == null || string.IsNullOrWhiteSpace(user.Municipality) ||
            (user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow)) return null;
        var stamp = principal.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (stamp != null && stamp != user.SecurityStamp) return null;
        return await (from membership in db.UserRoles
                      join role in db.Roles on membership.RoleId equals role.Id
                      where membership.UserId == user.Id && role.NormalizedName == "MUNICIPALOFFICIAL"
                      select membership).AnyAsync() ? user : null;
    }
}
