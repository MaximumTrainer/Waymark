using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Validators;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class WorkflowServiceTests
{
    [Fact]
    public async Task SubmitStepAsync_UsesConnectionPriority_WhenMultipleConnectionsCanMatch()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlow(includeUnconditionalConnection: true);
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        var response = await service.SubmitStepAsync(started.SessionId, started.CurrentNode!.Id, new SubmitStepRequest
        {
            Payload = new Dictionary<string, object?> { ["Country"] = "USA", ["FirstName"] = "Ada" }
        });

        Assert.False(response.IsCompleted);
        Assert.Equal("us-ssn", response.CurrentNode!.Key);
    }

    [Fact]
    public async Task SubmitStepAsync_BranchesToUsaNode_WhenCountryEqualsUsa()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlow();
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        var response = await service.SubmitStepAsync(started.SessionId, started.CurrentNode!.Id, new SubmitStepRequest
        {
            Payload = new Dictionary<string, object?> { ["Country"] = "USA", ["FirstName"] = "Ada" }
        });

        Assert.False(response.IsCompleted);
        Assert.Equal("us-ssn", response.CurrentNode!.Key);
    }

    [Fact]
    public async Task SubmitStepAsync_CompletesSession_WhenNoNextConnection()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlow();
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        var branch = await service.SubmitStepAsync(started.SessionId, started.CurrentNode!.Id, new SubmitStepRequest
        {
            Payload = new Dictionary<string, object?> { ["Country"] = "USA", ["FirstName"] = "Ada" }
        });

        var completed = await service.SubmitStepAsync(started.SessionId, branch.CurrentNode!.Id, new SubmitStepRequest
        {
            Payload = new Dictionary<string, object?> { ["Ssn"] = "123-45-6789" }
        });

        Assert.True(completed.IsCompleted);
        Assert.Null(completed.CurrentNode);
    }

    [Fact]
    public async Task SubmitStepAsync_ThrowsValidationException_WhenComplianceRuleJsonIsInvalid()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlow();
        flow.Nodes.First(x => x.IsStartNode).ComplianceRuleJson = "{invalid json";
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        await Assert.ThrowsAsync<ValidationException>(() => service.SubmitStepAsync(
            started.SessionId,
            started.CurrentNode!.Id,
            new SubmitStepRequest
            {
                Payload = new Dictionary<string, object?> { ["Country"] = "USA", ["FirstName"] = "Ada" }
            }));
    }

    private static WorkflowService CreateService(OnboardingDbContext dbContext)
    {
        return new WorkflowService(
            dbContext,
            new StartSessionRequestValidator(),
            new SubmitStepRequestValidator());
    }

    private static OnboardingDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new OnboardingDbContext(options);
    }

    private static Flow CreateFlow(bool includeUnconditionalConnection = false)
    {
        var flowId = Guid.NewGuid();
        var startNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "country-form",
            Title = "Country details",
            Type = NodeType.Form,
            IsStartNode = true,
            ComplianceRuleJson = "{\"requiredFields\":[\"Country\",\"FirstName\"]}"
        };

        var usaNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "us-ssn",
            Title = "SSN Form",
            Type = NodeType.Form,
            ComplianceRuleJson = "{\"requiredFields\":[\"Ssn\"]}"
        };

        var passportNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "passport-upload",
            Title = "Passport Upload",
            Type = NodeType.DocumentUpload
        };

        return new Flow
        {
            Id = flowId,
            Name = "Compliance onboarding",
            Description = "Branch by country",
            Nodes = new List<Node> { startNode, usaNode, passportNode },
            Connections = BuildConnections(flowId, startNode.Id, usaNode.Id, passportNode.Id, includeUnconditionalConnection)
        };
    }

    private static List<Connection> BuildConnections(
        Guid flowId,
        Guid startNodeId,
        Guid usaNodeId,
        Guid passportNodeId,
        bool includeUnconditionalConnection)
    {
        var connections = new List<Connection>
        {
            new()
            {
                FlowId = flowId,
                SourceNodeId = startNodeId,
                TargetNodeId = usaNodeId,
                ConditionField = "Country",
                ConditionOperator = ConditionOperator.Equals,
                ConditionValue = "USA",
                Priority = 0
            },
            new()
            {
                FlowId = flowId,
                SourceNodeId = startNodeId,
                TargetNodeId = passportNodeId,
                ConditionField = "Country",
                ConditionOperator = ConditionOperator.NotEquals,
                ConditionValue = "USA",
                Priority = 1
            }
        };

        if (includeUnconditionalConnection)
        {
            connections.Add(new Connection
            {
                FlowId = flowId,
                SourceNodeId = startNodeId,
                TargetNodeId = passportNodeId,
                Priority = 99
            });
        }

        return connections;
    }
}
