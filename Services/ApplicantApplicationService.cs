using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

// Recheck ownership and allowed transitions when a previously rendered applicant action is used.
public class ApplicantApplicationService(IServiceScopeFactory scopes)
{
    public async Task ChangeSubmissionAsync(ClaimsPrincipal principal, int applicationId, bool reapply)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.GetUserAsync(principal);
        var stamp = principal.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (principal.Identity?.IsAuthenticated != true || user == null || !await users.IsInRoleAsync(user, "BusinessOwner") ||
            await users.IsLockedOutAsync(user) || (stamp != null && stamp != user.SecurityStamp)) throw new UnauthorizedAccessException();
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId && a.UserId == user.Id)
            ?? throw new UnauthorizedAccessException();
        if (reapply)
        {
            if (application.Status is not ("Rejected" or "Withdrawn"))
                throw new ValidationException("This application is no longer eligible for reapplication. Refresh the page.");
            var municipality = await db.Municipalities.AsNoTracking().SingleOrDefaultAsync(m => m.Name == application.Municipality);
            if (municipality?.RoutingName == null)
                throw new ValidationException("This historical application cannot be resubmitted here. Please start a new application so its trading address can be checked.");
            await MunicipalityManagementService.RequireActiveRoutingAsync(db, municipality.RoutingName);
            application.Status = "Submitted";
            application.DateSubmitted = DateTime.Now;
            application.DecisionDateUtc = null;
            application.DecisionReason = null;
        }
        else
        {
            if (application.Status != "Submitted")
                throw new ValidationException("Only a submitted application can be withdrawn. Its status has changed; refresh the page.");
            application.Status = "Withdrawn";
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
