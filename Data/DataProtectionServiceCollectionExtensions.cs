using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Data;

public static class DataProtectionServiceCollectionExtensions
{
    public const string ApplicationName = "ProvincialBusinessLicensingSystem";

    public static IServiceCollection AddProductionDataProtection(
        this IServiceCollection services,
        IHostEnvironment environment,
        string databaseConnection)
    {
        if (!environment.IsProduction()) return services;

        services.AddDbContext<DataProtectionKeyContext>(options =>
            options.UseNpgsql(databaseConnection, npgsql =>
                npgsql.MigrationsAssembly("BusinessLicensing.PostgreSqlMigrations")));
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<DataProtectionKeyContext>();

        return services;
    }
}
