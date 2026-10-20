using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Data;

// Isolates shared production data-protection keys from the application's domain model.
public sealed class DataProtectionKeyContext(DbContextOptions<DataProtectionKeyContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
}
