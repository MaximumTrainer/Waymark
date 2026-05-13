using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Tests.TestHelpers;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class DocumentUploadServiceTests
{
    [Fact]
    public async Task StoreAsync_ReturnsStoredFileInfo_WithCorrectMetadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var env = new TestWebHostEnvironment(tempDir);
            var service = new LocalDocumentStorageService(env);

            var content = "hello world"u8.ToArray();
            using var stream = new MemoryStream(content);

            var info = await service.StoreAsync(stream, "test.txt", "text/plain");

            Assert.NotEmpty(info.FileId);
            Assert.Equal("test.txt", info.FileName);
            Assert.Equal("text/plain", info.ContentType);
            Assert.Equal(content.Length, info.SizeBytes);
            Assert.True(info.StoredAt <= DateTimeOffset.UtcNow);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetStreamAsync_ReturnsFileAndInfo_WhenFileExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var env = new TestWebHostEnvironment(tempDir);
            var service = new LocalDocumentStorageService(env);

            var content = "test content"u8.ToArray();
            using var storeStream = new MemoryStream(content);
            var stored = await service.StoreAsync(storeStream, "doc.txt", "text/plain");

            var (stream, info) = await service.GetStreamAsync(stored.FileId);
            await using (stream)
            {
                using var reader = new StreamReader(stream);
                var text = await reader.ReadToEndAsync();
                Assert.Equal("test content", text);
            }

            Assert.Equal(stored.FileId, info.FileId);
            Assert.Equal("doc.txt", info.FileName);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetStreamAsync_ThrowsNotFoundException_WhenFileDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var env = new TestWebHostEnvironment(tempDir);
            var service = new LocalDocumentStorageService(env);

            await Assert.ThrowsAsync<NotFoundException>(() =>
                service.GetStreamAsync("nonexistentfileid00000000000000"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_ReturnsIsSafe_ByDefault()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var env = new TestWebHostEnvironment(tempDir);
            var service = new LocalDocumentStorageService(env);

            var result = await service.ScanAsync("anyfileid");
            Assert.True(result.IsSafe);
            Assert.Null(result.ThreatName);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment(string rootPath) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = rootPath;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "Test";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = rootPath;
        public string EnvironmentName { get; set; } = "Test";
    }
}

public sealed class InMemorySessionEventEmitterTests
{
    [Fact]
    public async Task EmitAsync_CanBeReceivedBySubscriber()
    {
        var emitter = new InMemorySessionEventEmitter();
        var sessionId = Guid.NewGuid();

        // Subscribe first, then emit (subscriber picks up from channel)
        var subscribeTask = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await foreach (var evt in emitter.SubscribeAsync(sessionId, cts.Token))
            {
                return evt;
            }
            return null;
        });

        // Small delay to allow subscription to start reading
        await Task.Delay(50);
        await emitter.EmitAsync(sessionId, "step-advanced", new { sessionId });

        var received = await subscribeTask;
        Assert.NotNull(received);
        Assert.Equal("step-advanced", received!.EventType);
    }

    [Fact]
    public async Task EmitAsync_SessionCompleted_CompletesChannel()
    {
        var emitter = new InMemorySessionEventEmitter();
        var sessionId = Guid.NewGuid();

        var events = new List<SessionEvent>();
        var subscribeTask = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await foreach (var evt in emitter.SubscribeAsync(sessionId, cts.Token))
            {
                events.Add(evt);
            }
        });

        await Task.Delay(50);
        await emitter.EmitAsync(sessionId, "session-completed", new { sessionId });

        await subscribeTask;

        Assert.Single(events);
        Assert.Equal("session-completed", events[0].EventType);
    }

    [Fact]
    public async Task EmitAsync_SessionAbandoned_CompletesChannel()
    {
        var emitter = new InMemorySessionEventEmitter();
        var sessionId = Guid.NewGuid();

        var events = new List<SessionEvent>();
        var subscribeTask = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await foreach (var evt in emitter.SubscribeAsync(sessionId, cts.Token))
            {
                events.Add(evt);
            }
        });

        await Task.Delay(50);
        await emitter.EmitAsync(sessionId, "session-abandoned", new { sessionId });

        await subscribeTask;

        Assert.Single(events);
        Assert.Equal("session-abandoned", events[0].EventType);
    }
}

public sealed class WorkflowServiceEmitsEventsTests
{
    [Fact]
    public async Task SubmitStepAsync_EmitsStepAdvancedEvent_WhenNextNodeExists()
    {
        var dbContext = BuildDbContext();
        var flow = CreateTwoNodeFlow();
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var emitter = new InMemorySessionEventEmitter();
        var service = CreateService(dbContext, emitter);
        var started = await service.StartSessionAsync(new Application.Contracts.StartSessionRequest { FlowId = flow.Id });

        var eventsTask = CollectOneEvent(emitter, started.SessionId);
        await service.SubmitStepAsync(started.SessionId, started.CurrentNode!.Id,
            new Application.Contracts.SubmitStepRequest { Payload = new Dictionary<string, object?>() });

        var evt = await eventsTask;
        Assert.Equal("step-advanced", evt?.EventType);
    }

    [Fact]
    public async Task SubmitStepAsync_EmitsSessionCompletedEvent_WhenNoNextNode()
    {
        var dbContext = BuildDbContext();
        var flow = CreateSingleNodeFlow();
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var emitter = new InMemorySessionEventEmitter();
        var service = CreateService(dbContext, emitter);
        var started = await service.StartSessionAsync(new Application.Contracts.StartSessionRequest { FlowId = flow.Id });

        var eventsTask = CollectOneEvent(emitter, started.SessionId);
        await service.SubmitStepAsync(started.SessionId, started.CurrentNode!.Id,
            new Application.Contracts.SubmitStepRequest { Payload = new Dictionary<string, object?>() });

        var evt = await eventsTask;
        Assert.Equal("session-completed", evt?.EventType);
    }

    [Fact]
    public async Task AbandonSessionAsync_EmitsSessionAbandonedEvent()
    {
        var dbContext = BuildDbContext();
        var flow = CreateSingleNodeFlow();
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var emitter = new InMemorySessionEventEmitter();
        var service = CreateService(dbContext, emitter);
        var started = await service.StartSessionAsync(new Application.Contracts.StartSessionRequest { FlowId = flow.Id });

        var eventsTask = CollectOneEvent(emitter, started.SessionId);
        await service.AbandonSessionAsync(started.SessionId);

        var evt = await eventsTask;
        Assert.Equal("session-abandoned", evt?.EventType);
    }

    private static async Task<SessionEvent?> CollectOneEvent(ISessionEventEmitter emitter, Guid sessionId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in emitter.SubscribeAsync(sessionId, cts.Token))
        {
            return evt;
        }
        return null;
    }

    private static WorkflowService CreateService(OnboardingDbContext dbContext, ISessionEventEmitter emitter)
    {
        var customerService = new CustomerService(
            dbContext,
            new Application.Validators.CreateCustomerRequestValidator(),
            new Application.Validators.UpdateCustomerRequestValidator());

        return new WorkflowService(
            dbContext,
            new Application.Validators.StartSessionRequestValidator(),
            new Application.Validators.SubmitStepRequestValidator(),
            customerService,
            new ComplianceRuleEvaluator(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkflowService>.Instance,
            [],
            emitter,
            new NoOpWebhookService(),
            new NoOpDocumentStorageService());
    }

    private static OnboardingDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OnboardingDbContext(options);
    }

    private static Flow CreateSingleNodeFlow()
    {
        var flowId = Guid.NewGuid();
        var node = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "only-node",
            Title = "Only Node",
            Type = NodeType.Form,
            IsStartNode = true
        };
        return new Flow
        {
            Id = flowId,
            Name = "Single Node Flow",
            Nodes = new List<Node> { node },
            Connections = new List<Connection>()
        };
    }

    private static Flow CreateTwoNodeFlow()
    {
        var flowId = Guid.NewGuid();
        var node1 = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "node-1",
            Title = "Node 1",
            Type = NodeType.Form,
            IsStartNode = true
        };
        var node2 = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "node-2",
            Title = "Node 2",
            Type = NodeType.Form
        };
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            SourceNodeId = node1.Id,
            TargetNodeId = node2.Id
        };
        return new Flow
        {
            Id = flowId,
            Name = "Two Node Flow",
            Nodes = new List<Node> { node1, node2 },
            Connections = new List<Connection> { connection }
        };
    }
}

/// <summary>Test stub: stores nothing, never throws, safe scan.</summary>
internal sealed class NoOpDocumentStorageService : IDocumentStorageService
{
    public Task<StoredFileInfo> StoreAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
        => Task.FromResult(new StoredFileInfo(Guid.NewGuid().ToString("N"), fileName, contentType, 0, DateTimeOffset.UtcNow));

    public Task<(Stream Stream, StoredFileInfo Info)> GetStreamAsync(string fileId, CancellationToken cancellationToken = default)
    {
        Stream s = new MemoryStream();
        var info = new StoredFileInfo(fileId, "file", "application/octet-stream", 0, DateTimeOffset.UtcNow);
        return Task.FromResult((s, info));
    }

    public Task<ScanResult> ScanAsync(string fileId, CancellationToken cancellationToken = default)
        => Task.FromResult(new ScanResult(true, null));
}
