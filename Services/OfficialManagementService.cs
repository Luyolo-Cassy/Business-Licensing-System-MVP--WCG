using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public record OfficialSummary(string Id, string FullName, string Email, string Municipality, bool IsActive, bool NeedsSetup);
public record OfficialInvitation(string UserId, string FullName, string Email, string Code);

public class OfficialManagementService(IServiceScopeFactory scopes)
{
    private const string SetupPurpose = "MunicipalOfficialInitialSetup";
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new ValidationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
    private static async Task<UserManager<ApplicationUser>> AuthorizeAsync(IServiceProvider services, ClaimsPrincipal principal)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.GetUserAsync(principal);
        if (admin == null || !await users.IsInRoleAsync(admin, "DEDATAdmin")) throw new UnauthorizedAccessException();
        return users;
    }
    public async Task<List<OfficialSummary>> ListAsync(ClaimsPrincipal principal)
    {
        await using var scope = scopes.CreateAsyncScope();
        var users = await AuthorizeAsync(scope.ServiceProvider, principal);
        return (await users.GetUsersInRoleAsync("MunicipalOfficial"))
            .OrderBy(u => u.FullName).Select(u => new OfficialSummary(u.Id, u.FullName, u.Email ?? "", u.Municipality ?? "",
                !(u.LockoutEnabled && u.LockoutEnd > DateTimeOffset.UtcNow), u.PasswordHash == null)).ToList();
    }
    public async Task<List<Municipality>> ActiveMunicipalitiesAsync(ClaimsPrincipal principal)
    {
        await using var scope = scopes.CreateAsyncScope();
        await AuthorizeAsync(scope.ServiceProvider, principal);
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Municipalities.AsNoTracking()
            .Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync();
    }
    public async Task<OfficialInvitation?> SaveAsync(ClaimsPrincipal principal, string? id, string fullName, string email, int? municipalityId)
    {
        fullName = fullName.Trim(); email = email.Trim();
        if (fullName.Length == 0 || fullName.Length > 200) throw new ValidationException("Enter a full name of up to 200 characters.");
        if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email)) throw new ValidationException("Enter a valid email address.");
        await using var scope = scopes.CreateAsyncScope();
        var users = await AuthorizeAsync(scope.ServiceProvider, principal);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = id == null ? new ApplicationUser() : await users.FindByIdAsync(id)
            ?? throw new ValidationException("Official not found.");
        if (id != null && !await users.IsInRoleAsync(user, "MunicipalOfficial")) throw new ValidationException("Official not found.");
        var duplicate = await users.FindByEmailAsync(email);
        var duplicateName = await users.FindByNameAsync(email);
        if ((duplicate != null && duplicate.Id != user.Id) || (duplicateName != null && duplicateName.Id != user.Id))
            throw new ValidationException("An Identity account already uses this email address.");
        // A null selection on edit explicitly retains an existing inactive/legacy assignment.
        if (municipalityId != null)
        {
            var municipality = await db.Municipalities.AsNoTracking().SingleOrDefaultAsync(m => m.Id == municipalityId && m.IsActive)
                ?? throw new ValidationException("Select an active municipality. The selected municipality is no longer available.");
            user.Municipality = municipality.Name;
        }
        else if (id == null || string.IsNullOrWhiteSpace(user.Municipality)) throw new ValidationException("Select one active municipality.");
        user.FullName = fullName;
        if (id == null)
        {
            user.Email = email; user.UserName = email;
            Check(await users.CreateAsync(user));
            Check(await users.AddToRoleAsync(user, "MunicipalOfficial"));
        }
        else
        {
            if (user.Email != email) Check(await users.SetEmailAsync(user, email));
            if (user.UserName != email) Check(await users.SetUserNameAsync(user, email));
            Check(await users.UpdateAsync(user));
            Check(await users.UpdateSecurityStampAsync(user));
        }
        OfficialInvitation? invitation = null;
        if (!await users.HasPasswordAsync(user) && !await users.IsLockedOutAsync(user)) invitation = await InvitationAsync(users, user);
        await transaction.CommitAsync();
        return invitation;
    }
    public async Task SetActiveAsync(ClaimsPrincipal principal, string id, bool active)
    {
        await using var scope = scopes.CreateAsyncScope();
        var users = await AuthorizeAsync(scope.ServiceProvider, principal);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByIdAsync(id);
        if (user == null || !await users.IsInRoleAsync(user, "MunicipalOfficial")) throw new ValidationException("Official not found.");
        Check(await users.SetLockoutEnabledAsync(user, true));
        Check(await users.SetLockoutEndDateAsync(user, active ? null : DateTimeOffset.MaxValue));
        Check(await users.UpdateSecurityStampAsync(user));
        await transaction.CommitAsync();
    }
    public async Task<OfficialInvitation> GenerateInvitationAsync(ClaimsPrincipal principal, string id)
    {
        await using var scope = scopes.CreateAsyncScope();
        var users = await AuthorizeAsync(scope.ServiceProvider, principal);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByIdAsync(id);
        if (!await EligibleAsync(users, user)) throw new ValidationException("Setup links are only available for active Officials who have not set a password.");
        Check(await users.UpdateSecurityStampAsync(user!));
        var invitation = await InvitationAsync(users, user!);
        await transaction.CommitAsync();
        return invitation;
    }
    private static async Task<OfficialInvitation> InvitationAsync(UserManager<ApplicationUser> users, ApplicationUser user) =>
        new(user.Id, user.FullName, user.Email!, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            await users.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, SetupPurpose))));
    private static async Task<bool> EligibleAsync(UserManager<ApplicationUser> users, ApplicationUser? user) =>
        user != null && await users.IsInRoleAsync(user, "MunicipalOfficial") && !await users.HasPasswordAsync(user) && !await users.IsLockedOutAsync(user);
    private static string? Decode(string? code)
    {
        try { return string.IsNullOrEmpty(code) ? null : Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch (FormatException) { return null; }
    }
    public async Task<bool> ValidateSetupAsync(string? id, string? code)
    {
        if (id == null || Decode(code) is not string token) return false;
        await using var scope = scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(id);
        return await EligibleAsync(users, user) && await users.VerifyUserTokenAsync(user!, TokenOptions.DefaultProvider, SetupPurpose, token);
    }
    public async Task CompleteSetupAsync(string? id, string? code, string password)
    {
        await using var scope = scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = id == null ? null : await users.FindByIdAsync(id);
        if (Decode(code) is not string token || !await EligibleAsync(users, user) ||
            !await users.VerifyUserTokenAsync(user!, TokenOptions.DefaultProvider, SetupPurpose, token))
            throw new ValidationException("This setup link is invalid, expired, or already used. Contact your administrator for assistance.");
        Check(await users.AddPasswordAsync(user!, password));
        await transaction.CommitAsync();
    }
}
