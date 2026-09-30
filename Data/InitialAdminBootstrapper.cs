using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;

namespace BusinessLicensing_Practice.Data;

public sealed class InitialAdminBootstrapper(
    IConfiguration configuration,
    UserManager<ApplicationUser> users,
    ILogger<InitialAdminBootstrapper> logger)
{
    private const string SectionName = "AdminBootstrap";
    private const string AdminRole = "DEDATAdmin";

    public async Task BootstrapAsync()
    {
        var section = configuration.GetSection(SectionName);
        var email = section["Email"]?.Trim();
        var password = section["Password"];
        var fullName = section["FullName"]?.Trim();

        var values = new Dictionary<string, string?>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["FullName"] = fullName
        };
        var configuredKeys = values.Where(item => !string.IsNullOrWhiteSpace(item.Value)).Select(item => item.Key).ToList();
        if (configuredKeys.Count == 0)
        {
            logger.LogWarning(
                "Initial DEDAT administrator bootstrap is not configured. Set AdminBootstrap:Email, AdminBootstrap:Password and AdminBootstrap:FullName to provision the first administrator.");
            return;
        }

        if (configuredKeys.Count != values.Count)
        {
            var missingKeys = string.Join(", ", values.Keys.Except(configuredKeys).Select(key => $"{SectionName}:{key}"));
            logger.LogError(
                "Initial DEDAT administrator bootstrap configuration is incomplete. Missing settings: {MissingSettings}. No account was created or changed.",
                missingKeys);
            return;
        }

        var admin = await users.FindByEmailAsync(email!);
        if (admin != null)
        {
            if (!await users.IsEmailConfirmedAsync(admin))
            {
                admin.EmailConfirmed = true;
                EnsureSucceeded(await users.UpdateAsync(admin), "confirm the initial administrator email");
            }

            if (!await users.IsInRoleAsync(admin, AdminRole))
            {
                EnsureSucceeded(await users.AddToRoleAsync(admin, AdminRole), "assign the initial administrator role");
                logger.LogInformation("The configured existing account was assigned the DEDAT administrator role. Its password and profile were preserved.");
            }
            else
            {
                logger.LogInformation("The configured initial DEDAT administrator already exists. No account changes were made.");
            }

            return;
        }

        admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName!
        };

        EnsureSucceeded(await users.CreateAsync(admin, password!), "create the initial DEDAT administrator");
        EnsureSucceeded(await users.AddToRoleAsync(admin, AdminRole), "assign the initial administrator role");
        logger.LogInformation("The initial DEDAT administrator account was created successfully.");
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded) return;

        var errors = string.Join(", ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException($"Could not {operation}: {errors}");
    }
}
