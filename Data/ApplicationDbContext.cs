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
        public DbSet<MunicipalMessage> MunicipalMessages { get; set; }
        public DbSet<Municipality> Municipalities { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Municipality>(municipality =>
            {
                municipality.HasIndex(m => m.Name).IsUnique();
                municipality.HasIndex(m => m.RoutingName).IsUnique();
                // Migration-managed seeds run once, preserving later administrative edits.
                municipality.HasData(
                    new Municipality { Id = 1, Name = "Bergrivier Municipality", RoutingName = "Bergrivier Municipality", IsActive = true },
                    new Municipality { Id = 2, Name = "Cederberg Municipality", RoutingName = "Cederberg Municipality", IsActive = true },
                    new Municipality { Id = 3, Name = "Hessequa Municipality", RoutingName = "Hessequa Municipality", IsActive = true },
                    new Municipality { Id = 4, Name = "Swartland Municipality", RoutingName = "Swartland Municipality", IsActive = true },
                    new Municipality { Id = 5, Name = "Witzenberg Municipality", RoutingName = "Witzenberg Municipality", IsActive = true });
            });

            builder.Entity<MunicipalMessage>(message =>
            {
                message.Property(m => m.Content).HasColumnType("TEXT").IsRequired();
                message.HasOne(m => m.Application).WithMany()
                    .HasForeignKey(m => m.ApplicationId).OnDelete(DeleteBehavior.Cascade);
                message.HasOne(m => m.Sender).WithMany()
                    .HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.SetNull);
                message.HasIndex(m => new { m.ApplicationId, m.ReadAtUtc });
            });

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
