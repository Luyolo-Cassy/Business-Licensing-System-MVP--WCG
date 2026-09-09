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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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
