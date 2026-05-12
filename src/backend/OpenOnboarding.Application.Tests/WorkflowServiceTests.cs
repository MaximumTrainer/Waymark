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

    private static Flow CreateFlow()
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
            Connections = new List<Connection>
            {
                new()
                {
                    FlowId = flowId,
                    SourceNodeId = startNode.Id,
                    TargetNodeId = usaNode.Id,
                    ConditionField = "Country",
                    ConditionOperator = ConditionOperator.Equals,
                    ConditionValue = "USA"
                },
                new()
                {
                    FlowId = flowId,
                    SourceNodeId = startNode.Id,
                    TargetNodeId = passportNode.Id,
                    ConditionField = "Country",
                    ConditionOperator = ConditionOperator.NotEquals,
                    ConditionValue = "USA"
                }
            }
        };
    }
}
