using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Domain.Entities;

namespace OpenOnboarding.Infrastructure.Persistence;

public sealed class OnboardingDbContext(DbContextOptions<OnboardingDbContext> options) : DbContext(options)
{
    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Flow>().HasMany(x => x.Nodes).WithOne(x => x.Flow).HasForeignKey(x => x.FlowId);
        modelBuilder.Entity<Flow>().HasMany(x => x.Connections).WithOne(x => x.Flow).HasForeignKey(x => x.FlowId);

        modelBuilder.Entity<Session>()
            .HasOne(x => x.Flow)
            .WithMany()
            .HasForeignKey(x => x.FlowId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Session>()
            .HasOne(x => x.CustomerProfile)
            .WithMany(x => x.Sessions)
            .HasForeignKey(x => x.CustomerProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Submission>()
            .HasOne(x => x.Session)
            .WithMany(x => x.Submissions)
            .HasForeignKey(x => x.SessionId);

        modelBuilder.Entity<Webhook>()
            .HasOne(x => x.Flow)
            .WithMany()
            .HasForeignKey(x => x.FlowId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WebhookDelivery>()
            .HasOne(x => x.Webhook)
            .WithMany(x => x.Deliveries)
            .HasForeignKey(x => x.WebhookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
