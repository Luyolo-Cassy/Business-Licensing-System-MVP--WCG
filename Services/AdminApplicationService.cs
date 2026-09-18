using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public record AdminApplicationRow(int Id, string ApplicationNumber, string Applicant, string LicenceType,
    string? ApplicationType, string? Municipality, DateTime DateSubmitted, string Status);
public record AdminApplicationList(List<AdminApplicationRow> Applications, List<string> Municipalities, List<string> Statuses);
public record AdminCommunication(string SenderName, string Content, DateTime CreatedAtUtc, DateTime? ReadAtUtc);
public record AdminApplicationDetail(Application Application, string ApplicantName, string? ApplicantEmail,
    string? ApplicantTelephone, List<AdminCommunication> Communications);
public record ApplicationCount(string Name, int Count);
public record MunicipalityApplicationCount(string Name, int Count, bool? IsActive);
public record AdminApplicationReport(int Total, List<ApplicationCount> Statuses,
    List<MunicipalityApplicationCount> Municipalities, List<ApplicationCount> LicenceTypes, List<ApplicationCount> ApplicationTypes);

// Read-only projections. No application mutation or processing operations belong in this service.
public class AdminApplicationService(IServiceScopeFactory scopes)
{
    private static async Task<ApplicationDbContext> AuthorizeAsync(IServiceProvider services, ClaimsPrincipal principal)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.GetUserAsync(principal);
        if (admin == null || !await users.IsInRoleAsync(admin, "DEDATAdmin")) throw new UnauthorizedAccessException();
        return services.GetRequiredService<ApplicationDbContext>();
    }

    public async Task<AdminApplicationList> ListAsync(ClaimsPrincipal principal, string? search = null, string? municipality = null, string? status = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = await AuthorizeAsync(scope.ServiceProvider, principal);
        var rows = db.Applications.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            rows = rows.Where(a => a.ApplicationNumber.ToLower().Contains(term) ||
                (a.Details != null && a.Details.ApplicantFirstName != null && a.Details.ApplicantFirstName != ""
                    ? a.Details.ApplicantFirstName + " " + a.Details.ApplicantLastName
                    : a.Details != null && a.Details.ApplicantName != null && a.Details.ApplicantName != ""
                        ? a.Details.ApplicantName : a.User != null ? a.User.FullName : "").ToLower().Contains(term));
        }
        if (!string.IsNullOrEmpty(municipality)) rows = rows.Where(a => a.Municipality == municipality);
        if (!string.IsNullOrEmpty(status)) rows = rows.Where(a => a.Status == status);
        var applications = await rows.OrderByDescending(a => a.DateSubmitted).ThenByDescending(a => a.Id)
            .Select(a => new AdminApplicationRow(a.Id, a.ApplicationNumber,
                a.Details != null && a.Details.ApplicantFirstName != null && a.Details.ApplicantFirstName != "" ? a.Details.ApplicantFirstName + " " + a.Details.ApplicantLastName :
                    a.Details != null && a.Details.ApplicantName != null && a.Details.ApplicantName != "" ? a.Details.ApplicantName : a.User != null ? a.User.FullName : "",
                a.LicenceType, a.Details != null ? a.Details.ApplicationType : null, a.Municipality, a.DateSubmitted, a.Status)).ToListAsync();
        var municipalities = await db.Municipalities.Select(m => m.Name)
            .Union(db.Applications.Where(a => a.Municipality != null && a.Municipality != "").Select(a => a.Municipality!))
            .OrderBy(name => name).ToListAsync();
        var statuses = await db.Applications.Where(a => a.Status != "").Select(a => a.Status).Distinct().OrderBy(s => s).ToListAsync();
        return new(applications, municipalities, statuses);
    }

    public async Task<AdminApplicationDetail?> DetailAsync(ClaimsPrincipal principal, int id)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = await AuthorizeAsync(scope.ServiceProvider, principal);
        var application = await db.Applications.AsNoTracking().Include(a => a.Details).Include(a => a.Documents)
            .SingleOrDefaultAsync(a => a.Id == id);
        if (application == null) return null;
        // Select contact fields only; never load the Identity entity or its secrets for oversight.
        var applicant = await db.Users.Where(u => u.Id == application.UserId)
            .Select(u => new { u.FullName, u.Email, u.PhoneNumber }).SingleOrDefaultAsync();
        var communications = await db.MunicipalMessages.AsNoTracking().Where(m => m.ApplicationId == id)
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .Select(m => new AdminCommunication(m.SenderName, m.Content, m.CreatedAtUtc, m.ReadAtUtc)).ToListAsync();
        return new(application,
            string.IsNullOrWhiteSpace(ApplicationEntry.FullName(application.Details)) ? applicant?.FullName ?? "" : ApplicationEntry.FullName(application.Details),
            application.Details?.ApplicantEmail ?? applicant?.Email,
            application.Details?.ApplicantTelephone ?? applicant?.PhoneNumber, communications);
    }

    public async Task<AdminApplicationReport> ReportAsync(ClaimsPrincipal principal)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = await AuthorizeAsync(scope.ServiceProvider, principal);
        // Read only the reporting fields, including legacy/unassigned and inactive-municipality history.
        var applications = await db.Applications.AsNoTracking().Select(a => new
        {
            a.Status, a.Municipality, a.LicenceType, ApplicationType = a.Details != null ? a.Details.ApplicationType : null
        }).ToListAsync();
        var participating = await db.Municipalities.AsNoTracking().ToListAsync();
        List<ApplicationCount> Counts(IEnumerable<string?> names, string missing) => names
            .GroupBy(name => string.IsNullOrWhiteSpace(name) ? missing : name)
            .Select(g => new ApplicationCount(g.Key!, g.Count())).OrderBy(g => g.Name).ToList();
        var municipalityCounts = Counts(applications.Select(a => a.Municipality), "Not assigned");
        var municipalityNames = participating.Select(m => m.Name).Union(municipalityCounts.Select(m => m.Name));
        var municipalities = municipalityNames.OrderBy(name => name).Select(name => new MunicipalityApplicationCount(name,
            municipalityCounts.FirstOrDefault(m => m.Name == name)?.Count ?? 0,
            participating.FirstOrDefault(m => m.Name == name)?.IsActive)).ToList();
        return new(applications.Count, Counts(applications.Select(a => a.Status), "Not recorded"), municipalities,
            Counts(applications.Select(a => a.LicenceType), "Not recorded"), Counts(applications.Select(a => a.ApplicationType), "Not recorded"));
    }
}
