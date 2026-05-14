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
            var service = new LocalDocumentStorageService(env, new NullVirusScanService());

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
            var service = new LocalDocumentStorageService(env, new NullVirusScanService());

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
            var service = new LocalDocumentStorageService(env, new NullVirusScanService());

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
            var service = new LocalDocumentStorageService(env, new NullVirusScanService());

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
            null,
            new NoOpDocumentStorageService(),
            new NoOpMetricsService());
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
/// <summary>Test stub: always returns infected scan result.</summary>
internal sealed class InfectedDocumentStorageService : IDocumentStorageService
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
        => Task.FromResult(new ScanResult(false, "Eicar.Test.Virus"));
}

/// <summary>Test stub: scan always throws TimeoutException (simulates ClamAV unavailable).</summary>
internal sealed class UnavailableScanDocumentStorageService : IDocumentStorageService
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
        => Task.FromException<ScanResult>(new TimeoutException("ClamAV timed out"));
}

/// <summary>Test stub: records calls to IncrementSessionsStarted.</summary>
internal sealed class RecordingMetricsService : IMetricsService
{
    public int SessionsStarted { get; private set; }
    public void IncrementSessionsStarted(string flowId) => SessionsStarted++;
    public void IncrementSessionsCompleted(string flowId) { }
    public void IncrementWebhookDeliveries(string status) { }
    public void SetActiveSessions(int count) { }
}

public sealed class VirusScanWorkflowServiceTests
{
    [Fact]
    public async Task UploadDocumentsAsync_ThrowsScanFailedException_WhenFileIsInfected()
    {
        var (db, uploadNode, session) = BuildUploadScenario();
        var svc = BuildService(db, new InfectedDocumentStorageService());
        var upload = new DocumentUploadItem(new MemoryStream(new byte[] { 1 }), "test.pdf", "application/pdf", 1);

        await Assert.ThrowsAsync<ScanFailedException>(
            () => svc.UploadDocumentsAsync(session.Id, uploadNode.Id, [upload], long.MaxValue));
    }

    [Fact]
    public async Task UploadDocumentsAsync_ThrowsScanServiceUnavailableException_WhenScanTimesOut()
    {
        var (db, uploadNode, session) = BuildUploadScenario();
        var svc = BuildService(db, new UnavailableScanDocumentStorageService());
        var upload = new DocumentUploadItem(new MemoryStream(new byte[] { 1 }), "test.pdf", "application/pdf", 1);

        await Assert.ThrowsAsync<ScanServiceUnavailableException>(
            () => svc.UploadDocumentsAsync(session.Id, uploadNode.Id, [upload], long.MaxValue));
    }

    private static (OnboardingDbContext Db, Node UploadNode, Session Session) BuildUploadScenario()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new OnboardingDbContext(options);
        var flowId = Guid.NewGuid();
        var uploadNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "upload",
            Title = "Upload",
            Type = NodeType.DocumentUpload,
            IsStartNode = true
        };
        var flow = new Flow
        {
            Id = flowId,
            Name = "Scan Test",
            Nodes = new List<Node> { uploadNode }
        };
        db.Flows.Add(flow);
        var session = new Session
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Flow = flow,
            CurrentNodeId = uploadNode.Id,
            Status = SessionStatus.Started
        };
        db.Sessions.Add(session);
        db.SaveChanges();
        return (db, uploadNode, session);
    }

    private static WorkflowService BuildService(OnboardingDbContext db, IDocumentStorageService documentStorage)
    {
        var customerService = new CustomerService(db,
            new Application.Validators.CreateCustomerRequestValidator(),
            new Application.Validators.UpdateCustomerRequestValidator());
        return new WorkflowService(
            db,
            new Application.Validators.StartSessionRequestValidator(),
            new Application.Validators.SubmitStepRequestValidator(),
            customerService,
            new ComplianceRuleEvaluator(),
            NullLogger<WorkflowService>.Instance,
            [],
            new InMemorySessionEventEmitter(),
            new NoOpWebhookService(),
            null,
            documentStorage,
            new NoOpMetricsService());
    }
}

public sealed class MetricsWorkflowServiceTests
{
    [Fact]
    public async Task StartSessionAsync_CallsIncrementSessionsStarted()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new OnboardingDbContext(options);
        var flowId = Guid.NewGuid();
        var startNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "info",
            Title = "Info",
            Type = NodeType.Information,
            IsStartNode = true
        };
        var flow = new Flow
        {
            Id = flowId,
            Name = "Metrics Test",
            Nodes = new List<Node> { startNode }
        };
        db.Flows.Add(flow);
        await db.SaveChangesAsync();

        var metrics = new RecordingMetricsService();
        var customerService = new CustomerService(db,
            new Application.Validators.CreateCustomerRequestValidator(),
            new Application.Validators.UpdateCustomerRequestValidator());
        var svc = new WorkflowService(
            db,
            new Application.Validators.StartSessionRequestValidator(),
            new Application.Validators.SubmitStepRequestValidator(),
            customerService,
            new ComplianceRuleEvaluator(),
            NullLogger<WorkflowService>.Instance,
            [],
            new InMemorySessionEventEmitter(),
            new NoOpWebhookService(),
            null,
            new NoOpDocumentStorageService(),
            metrics);

        await svc.StartSessionAsync(new Application.Contracts.StartSessionRequest { FlowId = flow.Id });

        Assert.Equal(1, metrics.SessionsStarted);
    }
}

public sealed class MetricsEndpointAuthTests
{
    [Fact]
    public async Task GetMetrics_Returns401_WithoutApiKey()
    {
        await using var factory = TestWebAppFactory.Create();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/metrics");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}