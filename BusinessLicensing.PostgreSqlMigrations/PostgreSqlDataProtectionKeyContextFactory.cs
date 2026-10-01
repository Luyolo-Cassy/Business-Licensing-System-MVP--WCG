using BusinessLicensing_Practice.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BusinessLicensing.PostgreSqlMigrations;

public sealed class PostgreSqlDataProtectionKeyContextFactory
    : IDesignTimeDbContextFactory<DataProtectionKeyContext>
{
    private const string DesignTimeConnection =
        "Host=localhost;Port=5432;Database=business_licensing_design;Username=postgres;Password=design-time-only";

    public DataProtectionKeyContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DataProtectionKeyContext>()
            .UseNpgsql(DesignTimeConnection, npgsql =>
                npgsql.MigrationsAssembly(typeof(PostgreSqlDataProtectionKeyContextFactory).Assembly.FullName))
            .Options;

        return new DataProtectionKeyContext(options);
    }
}
