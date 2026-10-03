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
        public DbSet<ApplicationDraft> ApplicationDrafts { get; set; }
        public DbSet<ApplicationDraftDocument> ApplicationDraftDocuments { get; set; }
        public DbSet<MunicipalMessage> MunicipalMessages { get; set; }
        public DbSet<Municipality> Municipalities { get; set; }
        public DbSet<AiSettings> AiSettings { get; set; }
        public DbSet<ApplicationAuditLog> ApplicationAuditLogs { get; set; }
        public DbSet<ApplicationAiSummary> ApplicationAiSummaries { get; set; }

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

            builder.Entity<AiSettings>().HasData(new AiSettings
            {
                Id = BusinessLicensing_Practice.Models.AiSettings.SingletonId,
                DocumentValidationEnabled = false,
                ApplicationSummariesEnabled = false
            });

            builder.Entity<MunicipalMessage>(message =>
            {
                message.Property(m => m.Content).HasColumnType("TEXT").IsRequired();
                message.Property(m => m.MessageType).HasDefaultValue(ApplicationWorkflow.GeneralMessage);
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

            builder.Entity<Application>().Property(application => application.RevisionNumber).HasDefaultValue(1);

            builder.Entity<ApplicationDetails>()
                .HasIndex(details => details.ApplicationId)
                .IsUnique();

            builder.Entity<ApplicationDraft>(draft =>
            {
                draft.HasIndex(item => item.UserId).IsUnique()
                    .HasFilter("\"SourceApplicationId\" IS NULL");
                draft.HasIndex(item => item.SourceApplicationId).IsUnique()
                    .HasFilter("\"SourceApplicationId\" IS NOT NULL");
                draft.HasOne(item => item.User).WithMany()
                    .HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
                draft.HasOne(item => item.SourceApplication).WithMany()
                    .HasForeignKey(item => item.SourceApplicationId).OnDelete(DeleteBehavior.Cascade);
                draft.HasMany(item => item.Documents).WithOne(item => item.ApplicationDraft)
                    .HasForeignKey(item => item.ApplicationDraftId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ApplicationDraftDocument>()
                .HasIndex(item => new { item.ApplicationDraftId, item.DocumentType })
                .IsUnique();

            builder.Entity<ApplicationAuditLog>(audit =>
            {
                audit.Property(item => item.ApplicationNumber).HasMaxLength(100).IsRequired();
                audit.Property(item => item.ActorUserId).HasMaxLength(450).IsRequired();
                audit.Property(item => item.ActorDisplayName).HasMaxLength(200).IsRequired();
                audit.Property(item => item.ActorRole).HasMaxLength(100).IsRequired();
                audit.Property(item => item.Municipality).HasMaxLength(200).IsRequired();
                audit.Property(item => item.EventType).HasMaxLength(100).IsRequired();
                audit.Property(item => item.PreviousStatus).HasMaxLength(100);
                audit.Property(item => item.NewStatus).HasMaxLength(100);
                audit.Property(item => item.Summary).HasMaxLength(500).IsRequired();
                audit.Property(item => item.MetadataJson).HasColumnType("TEXT");
                audit.HasIndex(item => item.OccurredAtUtc);
                audit.HasIndex(item => item.ApplicationNumber);
                audit.HasIndex(item => item.ActorDisplayName);
                audit.HasIndex(item => item.Municipality);
                audit.HasIndex(item => item.EventType);
            });

            builder.Entity<ApplicationAiSummary>(summary =>
            {
                summary.Property(item => item.SummaryText).HasMaxLength(4000).IsRequired();
                summary.Property(item => item.GeneratedByUserId).HasMaxLength(450).IsRequired();
                summary.Property(item => item.GeneratedByName).HasMaxLength(200).IsRequired();
                summary.Property(item => item.Model).HasMaxLength(100).IsRequired();
                summary.HasOne(item => item.Application).WithMany()
                    .HasForeignKey(item => item.ApplicationId).OnDelete(DeleteBehavior.Cascade);
                summary.HasIndex(item => new { item.ApplicationId, item.RevisionNumber }).IsUnique();
            });
        }
    }
}
