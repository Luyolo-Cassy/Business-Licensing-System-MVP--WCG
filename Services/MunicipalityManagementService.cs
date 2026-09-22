using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public class MunicipalityManagementService(IServiceScopeFactory scopes)
{
    public const string UnavailableRoutingMessage = "Applications for the municipality identified by your trading address are currently unavailable. Please contact support.";

    private static async Task<ApplicationDbContext> AuthorizeAsync(IServiceProvider services, ClaimsPrincipal principal)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.GetUserAsync(principal);
        if (user == null || !await users.IsInRoleAsync(user, "DEDATAdmin"))
            throw new UnauthorizedAccessException();
        return services.GetRequiredService<ApplicationDbContext>();
    }

    public async Task<List<Municipality>> ListAsync(ClaimsPrincipal principal)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = await AuthorizeAsync(scope.ServiceProvider, principal);
        return await db.Municipalities.AsNoTracking().OrderBy(m => m.Name).ToListAsync();
    }

    public async Task<List<WesternCapeMunicipalityDefinition>> EligibleForAddAsync(ClaimsPrincipal principal)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = await AuthorizeAsync(scope.ServiceProvider, principal);
        var existing = await db.Municipalities.AsNoTracking()
            .Where(m => m.RoutingName != null).Select(m => m.RoutingName!).ToListAsync();
        var identities = existing.ToHashSet(StringComparer.Ordinal);
        return WesternCapeMunicipalityCatalog.Municipalities
            .Where(item => !identities.Contains(item.RoutingName))
            .OrderBy(item => item.DefaultName).ToList();
    }

    public async Task AddFromCatalogAsync(ClaimsPrincipal principal, string routingName)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = await AuthorizeAsync(scope.ServiceProvider, principal);
        var definition = WesternCapeMunicipalityCatalog.FindByRoutingName(routingName)
            ?? throw new ValidationException("Select a valid predefined Western Cape municipality.");
        if (await db.Municipalities.AnyAsync(m => m.RoutingName == definition.RoutingName))
            throw new ValidationException("This municipality already participates or has previously been added.");
        var normalizedName = definition.DefaultName.ToUpper();
        if (await db.Municipalities.AnyAsync(m => m.Name.ToUpper() == normalizedName))
            throw new ValidationException("A legacy municipality already uses this name. Resolve that record before adding the predefined municipality.");
        db.Municipalities.Add(new Municipality
        {
            Name = definition.DefaultName,
            RoutingName = definition.RoutingName,
            IsActive = true
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ValidationException("This municipality could not be added because its name or routing identity is already in use.");
        }
    }

    public async Task SaveAsync(ClaimsPrincipal principal, int? id, string name, string? originalName = null)
    {
        if (id is null) throw new ValidationException("Use the predefined municipality list to add a municipality.");
        name = name.Trim();
        if (name.Length == 0 || name.Length > 200)
            throw new ValidationException("Enter a municipality name of up to 200 characters.");
        await using var scope = scopes.CreateAsyncScope();
        var db = await AuthorizeAsync(scope.ServiceProvider, principal);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var existing = await db.Municipalities.AsNoTracking().ToListAsync();
        if (existing.Any(m => m.Id != id && string.Equals(m.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("A municipality with this name already exists.");
        // Reserve geographic aliases so adding/renaming cannot steal another routing identity.
        var reserved = WesternCapeMunicipalityCatalog.FindByReservedName(name);
        if (reserved != null && !existing.Any(m => m.Id == id && m.RoutingName == reserved.RoutingName))
            throw new ValidationException("This name is reserved for an existing geographically supported municipality.");

        var municipality = await db.Municipalities.SingleOrDefaultAsync(m => m.Id == id)
            ?? throw new ValidationException("The municipality no longer exists. Refresh the page.");
        if (municipality.Name != originalName)
            throw new ValidationException("This municipality was changed by another administrator. Refresh the page before editing.");
        if (municipality.Name != name)
        {
            // Do not merge previously unrelated legacy assignments into this municipality.
            if (await db.Applications.AnyAsync(a => a.Municipality == name) ||
                await db.Users.AnyAsync(u => u.Municipality == name))
                throw new ValidationException("That name is already used by existing municipality assignments.");
            var oldName = municipality.Name;
            await db.Applications.Where(a => a.Municipality == oldName)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Municipality, name));
            await db.Users.Where(u => u.Municipality == oldName)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Municipality, name));
            municipality.Name = name;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task SetActiveAsync(ClaimsPrincipal principal, int id, bool isActive)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = await AuthorizeAsync(scope.ServiceProvider, principal);
        if (await db.Municipalities.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsActive, isActive)) != 1)
            throw new ValidationException("The municipality no longer exists. Refresh the page.");
    }

    // Called within the submission transaction, before any application or file is saved.
    public static async Task<Municipality> RequireActiveRoutingAsync(ApplicationDbContext db, string canonicalName)
    {
        var municipality = await db.Municipalities.AsNoTracking().SingleOrDefaultAsync(m => m.RoutingName == canonicalName);
        if (municipality == null)
            throw new ValidationException(UnavailableRoutingMessage);
        if (!municipality.IsActive)
            throw new ValidationException($"Applications for {municipality.Name} are currently unavailable. Please contact the municipality for assistance.");
        return municipality;
    }
}
