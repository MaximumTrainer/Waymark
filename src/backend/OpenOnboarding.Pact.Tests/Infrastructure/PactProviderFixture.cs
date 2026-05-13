using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Pact.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory for Pact provider verification.
/// Binds Kestrel to a real TCP port so PactNet's Rust FFI verifier can connect.
/// Uses real PostgreSQL (available in CI) and configures test authentication.
/// </summary>
public sealed class PactProviderFixture : WebApplicationFactory<Program>
{
    private readonly int _port = GetFreePort();

    /// <summary>Real base URI that PactNet's verifier should call.</summary>
    public Uri ServerUri => new($"http://localhost:{_port}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Bind a real Kestrel TCP listener so PactNet's Rust FFI binary can connect.
        // UseKestrel() is called after UseTestServer() (registered internally by WebApplicationFactory),
        // making Kestrel the active IServer and giving us a real localhost port.
        builder.UseKestrel(opts => opts.ListenLocalhost(_port));

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OnboardingDb")
            ?? "Host=localhost;Port=5432;Database=onboarding_test;Username=postgres;Password=postgres";

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OnboardingDb"] = connectionString,
                ["Authentication:ApiKey"] = "test-api-key",
                ["Authentication:JwtAuthority"] = "",
                ["SessionTimeoutMinutes"] = "1440",
                ["DocumentUpload:MaxFileSizeBytes"] = "10485760"
            });
        });
    }

    /// <summary>
    /// Returns a dummy TestServer to satisfy WebApplicationFactory's internal requirement
    /// (it calls CreateServer after building the host, then calls host.Start() itself).
    /// The real server is Kestrel, bound above; this placeholder is never used for actual requests.
    /// </summary>
    protected override TestServer CreateServer(IHost host)
    {
        // The base WebApplicationFactory.CreateHost() calls host.Start() after CreateServer() returns,
        // which starts Kestrel on _port. We just need to return a valid TestServer placeholder here.
        return new TestServer(new WebHostBuilder().Configure(app =>
            app.Run(ctx =>
            {
                ctx.Response.StatusCode = 418;
                return Task.CompletedTask;
            })));
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
