using Microsoft.EntityFrameworkCore;
using BusinessLicensing_Practice.Models;

namespace BusinessLicensing_Practice.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Application> Applications { get; set; }
    }
}