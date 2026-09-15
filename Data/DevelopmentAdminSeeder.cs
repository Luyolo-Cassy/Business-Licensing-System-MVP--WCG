using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;

namespace BusinessLicensing_Practice.Data;

public static class DevelopmentAdminSeeder
{
    public static async Task SeedAsync(IHostEnvironment environment, UserManager<ApplicationUser> users)
    {
        if (!environment.IsDevelopment()) return;

        const string email = "dedat.admin@example.test";
        const string password = "DevOnly!DEDAT2026#";
        var admin = await users.FindByEmailAsync(email);
        if (admin != null)
        {
            // Never elevate an unrelated account that registered the reserved test address.
            var roles = await users.GetRolesAsync(admin);
            if (roles.Count != 1 || !roles.Contains("DEDATAdmin"))
                throw new InvalidOperationException("The development Admin email is already used by an account without exclusively the DEDATAdmin role.");
            return;
        }

        admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Development DEDAT Admin"
        };

        EnsureSucceeded(await users.CreateAsync(admin, password), "create development Admin");
        EnsureSucceeded(await users.AddToRoleAsync(admin, "DEDATAdmin"), "assign development Admin role");
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not {operation}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
    }
}
