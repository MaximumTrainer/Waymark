using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Pact.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory for Pact provider verification.
/// Replaces Npgsql with InMemory database and configures test authentication.
/// </summary>
public sealed class PactProviderFixture : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"pact-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:ApiKey"] = "test-api-key",
                ["Authentication:JwtAuthority"] = "",
                ["ConnectionStrings:OnboardingDb"] = "Host=test;Database=test",
                ["SessionTimeoutMinutes"] = "1440",
                ["DocumentUpload:MaxFileSizeBytes"] = "10485760"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Remove Npgsql DbContext registration
            var descriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<OnboardingDbContext>))
                .ToList();
            foreach (var d in descriptors) services.Remove(d);

            // Replace with InMemory DB
            services.AddDbContext<OnboardingDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }

    /// <summary>Seeds deterministic test data and returns the seeded IDs.</summary>
    public async Task<(Guid FlowId, Guid NodeId)> SeedFlowAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();

        var flowId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var nodeId = Guid.Parse("660e8400-e29b-41d4-a716-446655440001");

        if (!await db.Flows.AnyAsync(f => f.Id == flowId))
        {
            var flow = new OpenOnboarding.Domain.Entities.Flow
            {
                Id = flowId,
                Name = "Test Flow",
                Version = 1
            };
            var node = new OpenOnboarding.Domain.Entities.Node
            {
                Id = nodeId,
                FlowId = flowId,
                Key = "test-step",
                Title = "Test Step",
                Type = OpenOnboarding.Domain.Enums.NodeType.Form,
                IsStartNode = true
            };
            flow.Nodes.Add(node);
            db.Flows.Add(flow);
            await db.SaveChangesAsync();
        }

        return (flowId, nodeId);
    }

    /// <summary>Seeds a deterministic session and returns its ID.</summary>
    public async Task<Guid> SeedSessionAsync(Guid flowId, Guid nodeId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();

        var sessionId = Guid.Parse("770e8400-e29b-41d4-a716-446655440002");
        if (!await db.Sessions.AnyAsync(s => s.Id == sessionId))
        {
            db.Sessions.Add(new OpenOnboarding.Domain.Entities.Session
            {
                Id = sessionId,
                FlowId = flowId,
                CurrentNodeId = nodeId,
                Status = OpenOnboarding.Domain.Enums.SessionStatus.Started,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        return sessionId;
    }
}
