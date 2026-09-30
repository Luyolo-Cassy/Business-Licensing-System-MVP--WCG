using BusinessLicensing_Practice.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BusinessLicensing.PostgreSqlMigrations;

public sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string DesignTimeConnection =
        "Host=localhost;Port=5432;Database=businesslicensing_design;Username=design_only;Password=design_only";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(DesignTimeConnection, npgsql =>
                npgsql.MigrationsAssembly(typeof(PostgreSqlDesignTimeDbContextFactory).Assembly.FullName))
            .Options;

        return new ApplicationDbContext(options);
    }
}
