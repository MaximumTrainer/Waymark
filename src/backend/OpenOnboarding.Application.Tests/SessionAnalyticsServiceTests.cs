using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Tests.TestHelpers;
using OpenOnboarding.Application.Validators;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class SessionAnalyticsServiceTests
{
    [Fact]
    public async Task GetSubmissionsAsync_ReturnsEmptyList_WhenSessionHasNoSubmissions()
    {
        var db = BuildDbContext();
        var flow = CreateMinimalFlow();
        db.Flows.Add(flow);
        await db.SaveChangesAsync();

        var workflowService = CreateWorkflowService(db);
        var started = await workflowService.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        var service = new SessionAnalyticsService(db);
        var result = await service.GetSubmissionsAsync(started.SessionId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSubmissionsAsync_ReturnsSubmissionsInChronologicalOrder_ForCompletedSession()
    {
        var db = BuildDbContext();
        var flow = CreateTwoStepFlow();
        db.Flows.Add(flow);
        await db.SaveChangesAsync();

        var workflowService = CreateWorkflowService(db);
        var started = await workflowService.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        var step2 = await workflowService.SubmitStepAsync(started.SessionId, started.CurrentNode!.Id,
            new SubmitStepRequest { Payload = new Dictionary<string, object?> { ["name"] = "Alice" } });

        await workflowService.SubmitStepAsync(started.SessionId, step2.CurrentNode!.Id,
            new SubmitStepRequest { Payload = new Dictionary<string, object?> { ["email"] = "alice@example.com" } });

        var service = new SessionAnalyticsService(db);
        var submissions = await service.GetSubmissionsAsync(started.SessionId);

        Assert.Equal(2, submissions.Count);
        Assert.Equal("step-1", submissions[0].NodeKey);
        Assert.Equal("step-2", submissions[1].NodeKey);
        Assert.True(submissions[0].SubmittedAt <= submissions[1].SubmittedAt);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsCorrectDetail_ForStartedSession()
    {
        var db = BuildDbContext();
        var flow = CreateMinimalFlow();
        db.Flows.Add(flow);
        await db.SaveChangesAsync();

        var workflowService = CreateWorkflowService(db);
        var started = await workflowService.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        var service = new SessionAnalyticsService(db);
        var detail = await service.GetSessionAsync(started.SessionId);

        Assert.Equal(started.SessionId, detail.Id);
        Assert.Equal(flow.Id, detail.FlowId);
        Assert.Equal(SessionStatus.Started, detail.Status);
        Assert.NotNull(detail.CurrentNode);
    }

    [Fact]
    public async Task GetSessionsAsync_ReturnsAllSessions_WhenNoFilterApplied()
    {
        var db = BuildDbContext();
        var flow = CreateMinimalFlow();
        db.Flows.Add(flow);
        await db.SaveChangesAsync();

        var workflowService = CreateWorkflowService(db);
        await workflowService.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });
        await workflowService.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        var service = new SessionAnalyticsService(db);
        var result = await service.GetSessionsAsync(null, null, 1, 20);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetSessionsAsync_FiltersSessionsByFlowAndStatus()
    {
        var db = BuildDbContext();
        var flow1 = CreateMinimalFlow();
        var flow2 = CreateMinimalFlow();
        db.Flows.AddRange(flow1, flow2);
        await db.SaveChangesAsync();

        var workflowService = CreateWorkflowService(db);
        var s1 = await workflowService.StartSessionAsync(new StartSessionRequest { FlowId = flow1.Id });
        await workflowService.StartSessionAsync(new StartSessionRequest { FlowId = flow2.Id });

        // Abandon session for flow1
        await workflowService.AbandonSessionAsync(s1.SessionId);

        var service = new SessionAnalyticsService(db);
        var result = await service.GetSessionsAsync(flow1.Id, SessionStatus.Abandoned, 1, 20);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(flow1.Id, result.Items[0].FlowId);
        Assert.Equal(SessionStatus.Abandoned, result.Items[0].Status);
    }

    [Fact]
    public async Task GetSessionsAsync_CapsPageSizeAt100()
    {
        var db = BuildDbContext();
        var service = new SessionAnalyticsService(db);
        var result = await service.GetSessionsAsync(null, null, 1, 500);

        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public async Task GetFlowStatsAsync_ReturnsZeroedStats_WhenNoSessionsExist()
    {
        var db = BuildDbContext();
        var service = new SessionAnalyticsService(db);
        var stats = await service.GetFlowStatsAsync(Guid.NewGuid());

        Assert.Equal(0, stats.TotalSessions);
        Assert.Equal(0, stats.CompletedSessions);
        Assert.Equal(0, stats.AbandonedSessions);
        Assert.Equal(0, stats.AverageCompletionTimeSeconds);
        Assert.Empty(stats.DropOffByNodeKey);
    }

    [Fact]
    public async Task GetFlowStatsAsync_ReturnsCorrectStats_ForMixedSessions()
    {
        var db = BuildDbContext();
        var flow = CreateTwoStepFlow();
        db.Flows.Add(flow);
        await db.SaveChangesAsync();

        var workflowService = CreateWorkflowService(db);

        // Complete one session
        var s1 = await workflowService.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });
        var s1Step2 = await workflowService.SubmitStepAsync(s1.SessionId, s1.CurrentNode!.Id,
            new SubmitStepRequest { Payload = new Dictionary<string, object?> { ["name"] = "Alice" } });
        await workflowService.SubmitStepAsync(s1.SessionId, s1Step2.CurrentNode!.Id,
            new SubmitStepRequest { Payload = new Dictionary<string, object?> { ["email"] = "a@a.com" } });

        // Abandon one session
        var s2 = await workflowService.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });
        await workflowService.AbandonSessionAsync(s2.SessionId);

        // Leave one session in progress
        await workflowService.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        var service = new SessionAnalyticsService(db);
        var stats = await service.GetFlowStatsAsync(flow.Id);

        Assert.Equal(3, stats.TotalSessions);
        Assert.Equal(1, stats.CompletedSessions);
        Assert.Equal(1, stats.AbandonedSessions);
        Assert.True(stats.AverageCompletionTimeSeconds >= 0);
        Assert.Single(stats.DropOffByNodeKey);
    }

    private static OnboardingDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OnboardingDbContext(options);
    }

    private static WorkflowService CreateWorkflowService(OnboardingDbContext db)
    {
        var customerService = new CustomerService(
            db,
            new CreateCustomerRequestValidator(),
            new UpdateCustomerRequestValidator());

        return new WorkflowService(
            db,
            new StartSessionRequestValidator(),
            new SubmitStepRequestValidator(),
            customerService,
            new ComplianceRuleEvaluator(),
            NullLogger<WorkflowService>.Instance,
            [],
            new InMemorySessionEventEmitter(),
            new NoOpWebhookService(),
            new NoOpDocumentStorageService());
    }

    /// <summary>Single-step flow that has no outgoing connections so it completes immediately.</summary>
    private static Flow CreateMinimalFlow()
    {
        var flowId = Guid.NewGuid();
        return new Flow
        {
            Id = flowId,
            Name = "Minimal flow",
            Nodes = new List<Node>
            {
                new() { Id = Guid.NewGuid(), FlowId = flowId, Key = "step-1", Title = "Step 1", Type = NodeType.Form, IsStartNode = true }
            },
            Connections = new List<Connection>()
        };
    }

    /// <summary>Two-step flow with an unconditional connection between step-1 and step-2.</summary>
    private static Flow CreateTwoStepFlow()
    {
        var flowId = Guid.NewGuid();
        var node1 = new Node { Id = Guid.NewGuid(), FlowId = flowId, Key = "step-1", Title = "Step 1", Type = NodeType.Form, IsStartNode = true };
        var node2 = new Node { Id = Guid.NewGuid(), FlowId = flowId, Key = "step-2", Title = "Step 2", Type = NodeType.Form };

        return new Flow
        {
            Id = flowId,
            Name = "Two-step flow",
            Nodes = new List<Node> { node1, node2 },
            Connections = new List<Connection>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    FlowId = flowId,
                    SourceNodeId = node1.Id,
                    TargetNodeId = node2.Id,
                    Priority = 0
                }
            }
        };
    }
}
