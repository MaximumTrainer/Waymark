using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.ReadModels;

namespace OpenOnboarding.Infrastructure.Persistence;

public sealed class OnboardingDbContext(DbContextOptions<OnboardingDbContext> options) : DbContext(options)
{
    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<FlowVersion> FlowVersions => Set<FlowVersion>();
    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<SessionReadModel> SessionReadModels => Set<SessionReadModel>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Modified)
            {
                var updatedAtProp = entry.Metadata.FindProperty("UpdatedAt");
                if (updatedAtProp != null && !entry.Property("UpdatedAt").IsModified)
                    entry.Property("UpdatedAt").CurrentValue = now;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── Flow ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Flow>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            b.HasMany(x => x.Nodes).WithOne(x => x.Flow).HasForeignKey(x => x.FlowId);
            b.HasMany(x => x.Connections).WithOne(x => x.Flow).HasForeignKey(x => x.FlowId);
        });

        // ── FlowVersion ───────────────────────────────────────────────────
        modelBuilder.Entity<FlowVersion>(b =>
        {
            b.HasOne(x => x.Flow).WithMany(x => x.Versions).HasForeignKey(x => x.FlowId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.FlowId, x.VersionNumber }).IsUnique();
        });

        // ── Node ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Node>(b =>
        {
            b.Property(x => x.Key).HasMaxLength(100).IsRequired();
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            // Unique constraint: a flow cannot have two nodes with the same key (#57)
            b.HasIndex(x => new { x.FlowId, x.Key }).IsUnique();
            // Composite index for start-node lookup (#59)
            b.HasIndex(x => new { x.FlowId, x.IsStartNode });
        });

        // ── CustomerProfile ───────────────────────────────────────────────
        modelBuilder.Entity<CustomerProfile>(b =>
        {
            b.Property(x => x.ExternalCustomerId).HasMaxLength(200).IsRequired();
            b.Property(x => x.Country).HasMaxLength(10).IsRequired();
            b.Property(x => x.Email).HasMaxLength(320).IsRequired();
            // Unique external customer ID per integrator
            b.HasIndex(x => x.ExternalCustomerId).IsUnique();
        });

        // ── Session ───────────────────────────────────────────────────────
        modelBuilder.Entity<Session>(b =>
        {
            b.HasOne(x => x.Flow)
                .WithMany()
                .HasForeignKey(x => x.FlowId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CustomerProfile)
                .WithMany(x => x.Sessions)
                .HasForeignKey(x => x.CustomerProfileId)
                .OnDelete(DeleteBehavior.SetNull);
            // Index for status-based queries (e.g. session timeout service) (#59)
            b.HasIndex(x => x.Status);
            // Composite index for listing sessions by flow + status (#59)
            b.HasIndex(x => new { x.FlowId, x.Status });
        });

        // ── Submission ────────────────────────────────────────────────────
        modelBuilder.Entity<Submission>(b =>
        {
            b.HasOne(x => x.Session)
                .WithMany(x => x.Submissions)
                .HasForeignKey(x => x.SessionId);
        });

        // ── Webhook ───────────────────────────────────────────────────────
        modelBuilder.Entity<Webhook>(b =>
        {
            b.Property(x => x.Url).HasMaxLength(2048).IsRequired();
            b.Property(x => x.Secret).HasMaxLength(512).IsRequired();
            b.HasOne(x => x.Flow)
                .WithMany()
                .HasForeignKey(x => x.FlowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── WebhookDelivery ───────────────────────────────────────────────
        modelBuilder.Entity<WebhookDelivery>(b =>
        {
            b.HasOne(x => x.Webhook)
                .WithMany(x => x.Deliveries)
                .HasForeignKey(x => x.WebhookId)
                .OnDelete(DeleteBehavior.Cascade);
            // Index for filtering deliveries by status (#59)
            b.HasIndex(x => new { x.WebhookId, x.Status });
        });

        // ── SessionReadModel (CQRS read side) ─────────────────────────────
        modelBuilder.Entity<SessionReadModel>(b =>
        {
            b.ToTable("SessionReadModels");
            b.HasKey(x => x.Id);
            b.Property(x => x.FlowName).HasMaxLength(200);
            b.Property(x => x.CustomerEmail).HasMaxLength(320);
            b.Property(x => x.CustomerCountry).HasMaxLength(10);
            b.Property(x => x.ExternalCustomerId).HasMaxLength(200);
            b.Property(x => x.CurrentNodeKey).HasMaxLength(100);
            b.Property(x => x.CurrentNodeTitle).HasMaxLength(200);
            b.Property(x => x.StatusName).HasMaxLength(20);
            b.HasIndex(x => x.FlowId);
            b.HasIndex(x => x.StatusName);
            b.HasIndex(new[] { nameof(SessionReadModel.FlowId), nameof(SessionReadModel.StatusName) });
        });
    }
}
