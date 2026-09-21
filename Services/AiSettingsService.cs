using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public class AiSettingsService(IServiceScopeFactory scopes)
{
    public async Task<bool> IsDocumentValidationEnabledAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AiSettings.AsNoTracking()
            .Where(settings => settings.Id == AiSettings.SingletonId)
            .Select(settings => settings.DocumentValidationEnabled)
            .SingleAsync();
    }

    public async Task<bool> GetDocumentValidationEnabledAsync(ClaimsPrincipal principal)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = await AuthorizeAsync(scope.ServiceProvider, principal);
        return await db.AiSettings.AsNoTracking()
            .Where(settings => settings.Id == AiSettings.SingletonId)
            .Select(settings => settings.DocumentValidationEnabled)
            .SingleAsync();
    }

    public async Task SetDocumentValidationEnabledAsync(ClaimsPrincipal principal, bool enabled)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = await AuthorizeAsync(scope.ServiceProvider, principal);
        if (await db.AiSettings.Where(settings => settings.Id == AiSettings.SingletonId)
            .ExecuteUpdateAsync(update => update.SetProperty(settings => settings.DocumentValidationEnabled, enabled)) != 1)
            throw new InvalidOperationException("AI settings are unavailable.");
    }

    private static async Task<ApplicationDbContext> AuthorizeAsync(IServiceProvider services, ClaimsPrincipal principal)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.GetUserAsync(principal);
        if (user == null || !await users.IsInRoleAsync(user, "DEDATAdmin"))
            throw new UnauthorizedAccessException();
        return services.GetRequiredService<ApplicationDbContext>();
    }
}
