using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Validators;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Tests.TestHelpers;
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

    // Contains
    [Fact]
    public async Task EvaluateCondition_Contains_ReturnsTrueWhenFieldContainsValue()
    {
        var result = await EvaluateSingleConditionAsync("CompanyName", ConditionOperator.Contains, "corp", new Dictionary<string, object?> { ["CompanyName"] = "Acme Corp" });
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateCondition_Contains_ReturnsFalseWhenFieldDoesNotContainValue()
    {
        var result = await EvaluateSingleConditionAsync("CompanyName", ConditionOperator.Contains, "xyz", new Dictionary<string, object?> { ["CompanyName"] = "Acme Corp" });
        Assert.False(result);
    }

    [Fact]
    public async Task EvaluateCondition_Contains_ReturnsTrueWhenConditionValueIsEmpty()
    {
        var result = await EvaluateSingleConditionAsync("CompanyName", ConditionOperator.Contains, "", new Dictionary<string, object?> { ["CompanyName"] = "Acme Corp" });
        Assert.True(result);
    }

    // NotContains
    [Fact]
    public async Task EvaluateCondition_NotContains_ReturnsTrueWhenFieldDoesNotContainValue()
    {
        var result = await EvaluateSingleConditionAsync("CompanyName", ConditionOperator.NotContains, "xyz", new Dictionary<string, object?> { ["CompanyName"] = "Acme Corp" });
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateCondition_NotContains_ReturnsFalseWhenFieldContainsValue()
    {
        var result = await EvaluateSingleConditionAsync("CompanyName", ConditionOperator.NotContains, "corp", new Dictionary<string, object?> { ["CompanyName"] = "Acme Corp" });
        Assert.False(result);
    }

    // StartsWith
    [Fact]
    public async Task EvaluateCondition_StartsWith_ReturnsTrueWhenFieldStartsWithValue()
    {
        var result = await EvaluateSingleConditionAsync("CompanyName", ConditionOperator.StartsWith, "acme", new Dictionary<string, object?> { ["CompanyName"] = "Acme Corp" });
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateCondition_StartsWith_ReturnsFalseWhenFieldDoesNotStartWithValue()
    {
        var result = await EvaluateSingleConditionAsync("CompanyName", ConditionOperator.StartsWith, "corp", new Dictionary<string, object?> { ["CompanyName"] = "Acme Corp" });
        Assert.False(result);
    }

    // EndsWith
    [Fact]
    public async Task EvaluateCondition_EndsWith_ReturnsTrueWhenFieldEndsWithValue()
    {
        var result = await EvaluateSingleConditionAsync("CompanyName", ConditionOperator.EndsWith, "CORP", new Dictionary<string, object?> { ["CompanyName"] = "Acme Corp" });
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateCondition_EndsWith_ReturnsFalseWhenFieldDoesNotEndWithValue()
    {
        var result = await EvaluateSingleConditionAsync("CompanyName", ConditionOperator.EndsWith, "acme", new Dictionary<string, object?> { ["CompanyName"] = "Acme Corp" });
        Assert.False(result);
    }

    // GreaterThan
    [Fact]
    public async Task EvaluateCondition_GreaterThan_ReturnsTrueWhenFieldIsGreater()
    {
        var result = await EvaluateSingleConditionAsync("AnnualRevenue", ConditionOperator.GreaterThan, "1000000", new Dictionary<string, object?> { ["AnnualRevenue"] = "5000000" });
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateCondition_GreaterThan_ReturnsFalseWhenFieldIsEqual()
    {
        var result = await EvaluateSingleConditionAsync("AnnualRevenue", ConditionOperator.GreaterThan, "1000000", new Dictionary<string, object?> { ["AnnualRevenue"] = "1000000" });
        Assert.False(result);
    }

    [Fact]
    public async Task EvaluateCondition_GreaterThan_ReturnsFalseWhenFieldIsNonNumeric()
    {
        var result = await EvaluateSingleConditionAsync("AnnualRevenue", ConditionOperator.GreaterThan, "1000000", new Dictionary<string, object?> { ["AnnualRevenue"] = "notanumber" });
        Assert.False(result);
    }

    // LessThan
    [Fact]
    public async Task EvaluateCondition_LessThan_ReturnsTrueWhenFieldIsLess()
    {
        var result = await EvaluateSingleConditionAsync("Score", ConditionOperator.LessThan, "50", new Dictionary<string, object?> { ["Score"] = "30" });
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateCondition_LessThan_ReturnsFalseWhenFieldIsEqual()
    {
        var result = await EvaluateSingleConditionAsync("Score", ConditionOperator.LessThan, "50", new Dictionary<string, object?> { ["Score"] = "50" });
        Assert.False(result);
    }

    [Fact]
    public async Task EvaluateCondition_LessThan_ReturnsFalseWhenFieldIsNonNumeric()
    {
        var result = await EvaluateSingleConditionAsync("Score", ConditionOperator.LessThan, "50", new Dictionary<string, object?> { ["Score"] = "high" });
        Assert.False(result);
    }

    // GreaterThanOrEqual
    [Fact]
    public async Task EvaluateCondition_GreaterThanOrEqual_ReturnsTrueWhenFieldIsEqual()
    {
        var result = await EvaluateSingleConditionAsync("Score", ConditionOperator.GreaterThanOrEqual, "100", new Dictionary<string, object?> { ["Score"] = "100" });
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateCondition_GreaterThanOrEqual_ReturnsFalseWhenFieldIsLess()
    {
        var result = await EvaluateSingleConditionAsync("Score", ConditionOperator.GreaterThanOrEqual, "100", new Dictionary<string, object?> { ["Score"] = "99" });
        Assert.False(result);
    }

    // LessThanOrEqual
    [Fact]
    public async Task EvaluateCondition_LessThanOrEqual_ReturnsTrueWhenFieldIsEqual()
    {
        var result = await EvaluateSingleConditionAsync("Score", ConditionOperator.LessThanOrEqual, "100", new Dictionary<string, object?> { ["Score"] = "100" });
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateCondition_LessThanOrEqual_ReturnsFalseWhenFieldIsGreater()
    {
        var result = await EvaluateSingleConditionAsync("Score", ConditionOperator.LessThanOrEqual, "100", new Dictionary<string, object?> { ["Score"] = "101" });
        Assert.False(result);
    }

    // MatchesRegex
    [Fact]
    public async Task EvaluateCondition_MatchesRegex_ReturnsTrueWhenFieldMatchesPattern()
    {
        var result = await EvaluateSingleConditionAsync("Email", ConditionOperator.MatchesRegex, @"^[^@]+@[^@]+\.[^@]+$", new Dictionary<string, object?> { ["Email"] = "user@example.com" });
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateCondition_MatchesRegex_ReturnsFalseWhenFieldDoesNotMatchPattern()
    {
        var result = await EvaluateSingleConditionAsync("Email", ConditionOperator.MatchesRegex, @"^\d+$", new Dictionary<string, object?> { ["Email"] = "user@example.com" });
        Assert.False(result);
    }

    [Fact]
    public async Task EvaluateCondition_MatchesRegex_ReturnsFalseWhenPatternIsInvalid()
    {
        var result = await EvaluateSingleConditionAsync("Email", ConditionOperator.MatchesRegex, @"[invalid(", new Dictionary<string, object?> { ["Email"] = "user@example.com" });
        Assert.False(result);
    }

    [Fact]
    public async Task EvaluateCondition_MatchesRegex_ReturnsFalseOnCatastrophicBacktracking()
    {
        // Pattern known to cause catastrophic backtracking
        var result = await EvaluateSingleConditionAsync("Input", ConditionOperator.MatchesRegex, @"(a+)+$", new Dictionary<string, object?> { ["Input"] = "aaaaaaaaaaaaaaaaaaaaaaaaaab" });
        Assert.False(result);
    }

    [Fact]
    public async Task AbandonSessionAsync_ReturnsAbandonedResponse_WhenSessionIsStarted()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlow();
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        var result = await service.AbandonSessionAsync(started.SessionId);

        Assert.False(result.IsCompleted);
        Assert.Null(result.CurrentNode);
        Assert.Equal(started.SessionId, result.SessionId);

        var session = await dbContext.Sessions.FindAsync(started.SessionId);
        Assert.Equal(SessionStatus.Abandoned, session!.Status);
    }

    [Fact]
    public async Task AbandonSessionAsync_ThrowsConflictException_WhenSessionIsCompleted()
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

        await service.SubmitStepAsync(started.SessionId, branch.CurrentNode!.Id, new SubmitStepRequest
        {
            Payload = new Dictionary<string, object?> { ["Ssn"] = "123-45-6789" }
        });

        await Assert.ThrowsAsync<ConflictException>(() => service.AbandonSessionAsync(started.SessionId));
    }

    [Fact]
    public async Task AbandonSessionAsync_IsIdempotent_WhenSessionIsAlreadyAbandoned()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlow();
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        await service.AbandonSessionAsync(started.SessionId);
        var second = await service.AbandonSessionAsync(started.SessionId);

        Assert.False(second.IsCompleted);
        Assert.Null(second.CurrentNode);
    }

    [Fact]
    public async Task SessionTimeoutService_AbandonsStartedSessions_OlderThanTimeout()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlow();
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        // Back-date the session so it appears timed out (2 minutes ago, timeout = 1 minute)
        var session = await dbContext.Sessions.FindAsync(started.SessionId);
        session!.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await dbContext.SaveChangesAsync();

        var scopeFactory = new TestServiceScopeFactory(dbContext);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SessionTimeoutMinutes"] = "1" })
            .Build();

        var timeoutService = new SessionTimeoutService(scopeFactory, config, NullLogger<SessionTimeoutService>.Instance);
        await timeoutService.CheckAndAbandonAsync(1);

        await dbContext.Entry(session).ReloadAsync();
        Assert.Equal(SessionStatus.Abandoned, session.Status);
    }

    [Fact]
    public async Task SessionTimeoutService_DoesNotAbandonSessions_WhenUpdatedAtIsWithinTimeout()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlow();
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });

        // Session was just created — well within a 60-minute timeout window
        var session = await dbContext.Sessions.FindAsync(started.SessionId);

        var scopeFactory = new TestServiceScopeFactory(dbContext);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SessionTimeoutMinutes"] = "60" })
            .Build();

        var timeoutService = new SessionTimeoutService(scopeFactory, config, NullLogger<SessionTimeoutService>.Instance);
        await timeoutService.CheckAndAbandonAsync(60);

        await dbContext.Entry(session!).ReloadAsync();
        Assert.Equal(SessionStatus.Started, session!.Status);
    }

    private sealed class TestServiceScopeFactory(OnboardingDbContext dbContext) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TestServiceScope(dbContext);
    }

    private sealed class TestServiceScope(OnboardingDbContext dbContext) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new TestServiceProvider(dbContext);
        public void Dispose() { }
    }

    private sealed class TestServiceProvider(OnboardingDbContext dbContext) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(OnboardingDbContext) ? dbContext : null;
    }

    private static WorkflowService CreateService(OnboardingDbContext dbContext, IEnumerable<ILogicNodeExecutor>? executors = null)
    {
        var customerService = new CustomerService(
            dbContext,
            new CreateCustomerRequestValidator(),
            new UpdateCustomerRequestValidator());

        return new WorkflowService(
            dbContext,
            new StartSessionRequestValidator(),
            new SubmitStepRequestValidator(),
            customerService,
            new ComplianceRuleEvaluator(),
            NullLogger<WorkflowService>.Instance,
            executors ?? [],
            new InMemorySessionEventEmitter(),
            new NoOpDocumentStorageService());
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

    /// <summary>
    /// Helper that creates a minimal two-node flow with a single conditional connection,
    /// submits the given payload, and returns whether the target node was reached.
    /// </summary>
    private static async Task<bool> EvaluateSingleConditionAsync(
        string conditionField,
        ConditionOperator conditionOperator,
        string conditionValue,
        IReadOnlyDictionary<string, object?> payload)
    {
        var dbContext = BuildDbContext();
        var flowId = Guid.NewGuid();

        var startNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "start",
            Title = "Start",
            Type = NodeType.Form,
            IsStartNode = true
        };

        var targetNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "target",
            Title = "Target",
            Type = NodeType.Form
        };

        var flow = new Flow
        {
            Id = flowId,
            Name = "Condition test flow",
            Nodes = new List<Node> { startNode, targetNode },
            Connections = new List<Connection>
            {
                new()
                {
                    FlowId = flowId,
                    SourceNodeId = startNode.Id,
                    TargetNodeId = targetNode.Id,
                    ConditionField = conditionField,
                    ConditionOperator = conditionOperator,
                    ConditionValue = conditionValue,
                    Priority = 0
                }
            }
        };

        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flowId });

        var response = await service.SubmitStepAsync(started.SessionId, started.CurrentNode!.Id, new SubmitStepRequest
        {
            Payload = new Dictionary<string, object?>(payload)
        });

        return !response.IsCompleted && response.CurrentNode?.Key == "target";
    }
}
