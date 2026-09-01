using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using BusinessLicensing_Practice.Models;

namespace BusinessLicensing_Practice.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Application> Applications { get; set; }
        public DbSet<ApplicationDocument> ApplicationDocuments { get; set; }
        public DbSet<ApplicationDetails> ApplicationDetails { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Application>()
                .HasOne(application => application.Details)
                .WithOne(details => details.Application)
                .HasForeignKey<ApplicationDetails>(details => details.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ApplicationDetails>()
                .HasIndex(details => details.ApplicationId)
                .IsUnique();
        }
    }
}
