using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
/// Starts a real Kestrel TCP proxy in front of the in-memory TestServer so that
/// PactNet's Rust FFI verifier can connect via a real TCP socket.
/// </summary>
public sealed class PactProviderFixture : WebApplicationFactory<Program>
{
    private readonly int _port = GetFreePort();
    private IHost? _proxy;

    /// <summary>Real base URI that PactNet's verifier should call.</summary>
    public Uri ServerUri => new($"http://localhost:{_port}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

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
    /// Starts the in-memory TestServer normally, then starts a Kestrel TCP proxy that
    /// forwards requests to the TestServer. PactNet's Rust FFI verifier connects to the
    /// Kestrel proxy (a real TCP socket); the app logic runs in the TestServer.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testHost = base.CreateHost(builder);

        var handler = testHost.GetTestServer().CreateHandler();
        var baseAddress = testHost.GetTestServer().BaseAddress;
        var proxyClient = new HttpClient(handler) { BaseAddress = baseAddress };

        _proxy = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseKestrel(opts => opts.ListenLocalhost(_port));
                web.Configure(app => app.Run(ctx => ProxyRequestAsync(ctx, proxyClient)));
            })
            .Build();
        _proxy.Start();

        return testHost;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _proxy?.StopAsync(cts.Token).GetAwaiter().GetResult();
            _proxy?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static async Task ProxyRequestAsync(HttpContext ctx, HttpClient client)
    {
        var targetUri = new Uri(client.BaseAddress!, ctx.Request.Path.Value + ctx.Request.QueryString.Value);
        using var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), targetUri);

        foreach (var header in ctx.Request.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value);
        }

        if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            request.Content = new StreamContent(ctx.Request.Body);
            if (ctx.Request.ContentType is not null)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
        ctx.Response.StatusCode = (int)response.StatusCode;

        foreach (var header in response.Headers)
            foreach (var value in header.Value)
                ctx.Response.Headers.Append(header.Key, value);

        foreach (var header in response.Content.Headers)
            foreach (var value in header.Value)
                ctx.Response.Headers.Append(header.Key, value);

        await response.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
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
        var node2Id = Guid.Parse("880e8400-e29b-41d4-a716-446655440003");

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
                Title = "Step Title",
                Type = OpenOnboarding.Domain.Enums.NodeType.Form,
                IsStartNode = true,
                JsonContent = "{}"
            };
            // Second node so submitting node1 advances the session (not completes it),
            // ensuring GET /next always returns a non-null currentNode regardless of interaction order.
            var node2 = new OpenOnboarding.Domain.Entities.Node
            {
                Id = node2Id,
                FlowId = flowId,
                Key = "test-step-2",
                Title = "Step Title 2",
                Type = OpenOnboarding.Domain.Enums.NodeType.Form,
                JsonContent = "{}"
            };
            var connection = new OpenOnboarding.Domain.Entities.Connection
            {
                FlowId = flowId,
                SourceNodeId = nodeId,
                TargetNodeId = node2Id,
                Priority = 0
            };
            flow.Nodes.Add(node);
            flow.Nodes.Add(node2);
            flow.Connections.Add(connection);
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
