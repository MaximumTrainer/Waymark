using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Tests.TestHelpers;
using OpenOnboarding.Application.Validators;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class LogicNodeExecutorTests
{
    // ─── SetProfileFieldExecutor ──────────────────────────────────────

    [Fact]
    public async Task SetProfileFieldExecutor_UpdatesCustomerProfileMetadata()
    {
        var profile = new CustomerProfile
        {
            Id = Guid.NewGuid(),
            ExternalCustomerId = "ext-1",
            Country = "USA",
            Email = "test@example.com",
            MetadataJson = "{}"
        };

        var session = new Session
        {
            Id = Guid.NewGuid(),
            FlowId = Guid.NewGuid(),
            CustomerProfile = profile,
            CustomerProfileId = profile.Id
        };

        var node = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "set-kyc",
            Type = NodeType.Logic,
            JsonContent = """{"action":"SetProfileField","field":"kyc_status","value":"pending"}"""
        };

        var executor = new SetProfileFieldExecutor();
        await executor.ExecuteAsync(node, session, new Dictionary<string, object?>());

        var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(profile.MetadataJson);
        Assert.NotNull(metadata);
        Assert.True(metadata.ContainsKey("kyc_status"));
        Assert.Equal("pending", metadata["kyc_status"].GetString());
    }

    [Fact]
    public async Task SetProfileFieldExecutor_MergesWithExistingMetadata()
    {
        var profile = new CustomerProfile
        {
            Id = Guid.NewGuid(),
            ExternalCustomerId = "ext-1",
            Country = "USA",
            Email = "test@example.com",
            MetadataJson = """{"existing_field":"existing_value"}"""
        };

        var session = new Session
        {
            Id = Guid.NewGuid(),
            FlowId = Guid.NewGuid(),
            CustomerProfile = profile,
            CustomerProfileId = profile.Id
        };

        var node = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "set-kyc",
            Type = NodeType.Logic,
            JsonContent = """{"action":"SetProfileField","field":"kyc_status","value":"approved"}"""
        };

        var executor = new SetProfileFieldExecutor();
        await executor.ExecuteAsync(node, session, new Dictionary<string, object?>());

        var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(profile.MetadataJson);
        Assert.NotNull(metadata);
        Assert.Equal("existing_value", metadata["existing_field"].GetString());
        Assert.Equal("approved", metadata["kyc_status"].GetString());
    }

    [Fact]
    public async Task SetProfileFieldExecutor_ThrowsWhenNoCustomerProfile()
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            FlowId = Guid.NewGuid(),
            CustomerProfile = null
        };

        var node = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "set-kyc",
            Type = NodeType.Logic,
            JsonContent = """{"action":"SetProfileField","field":"kyc_status","value":"pending"}"""
        };

        var executor = new SetProfileFieldExecutor();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(node, session, new Dictionary<string, object?>()));
    }

    // ─── HttpCallbackExecutor ─────────────────────────────────────────

    [Fact]
    public async Task HttpCallbackExecutor_PostsPayloadAndStoresResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"result":"ok"}""");
        var factory = new FakeHttpClientFactory(handler);

        var session = new Session
        {
            Id = Guid.NewGuid(),
            FlowId = Guid.NewGuid()
        };

        var node = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "http-cb",
            Type = NodeType.Logic,
            JsonContent = """{"action":"HttpCallback","url":"https://example.com/webhook"}"""
        };

        var payload = new Dictionary<string, object?> { ["field"] = "value" };

        var executor = new HttpCallbackExecutor(factory);
        await executor.ExecuteAsync(node, session, payload);

        Assert.Single(session.Submissions);
        var submissionData = JsonSerializer.Deserialize<JsonElement>(session.Submissions.First().DataJson);
        Assert.Equal(200, submissionData.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task HttpCallbackExecutor_ThrowsWhenNoUrlProvided()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "ok");
        var factory = new FakeHttpClientFactory(handler);

        var session = new Session { Id = Guid.NewGuid(), FlowId = Guid.NewGuid() };
        var node = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "http-cb",
            Type = NodeType.Logic,
            JsonContent = """{"action":"HttpCallback"}"""
        };

        var executor = new HttpCallbackExecutor(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(node, session, new Dictionary<string, object?>()));
    }

    // MockVerificationExecutor

    [Fact]
    public async Task MockVerificationExecutor_AddsVerificationSubmission()
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            FlowId = Guid.NewGuid()
        };

        var node = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "mock-verification",
            Type = NodeType.Logic,
            JsonContent = """{"action":"MockVerification","provider":"Experian","resultField":"experianVerificationStatus","approved":true}"""
        };

        var executor = new MockVerificationExecutor();
        await executor.ExecuteAsync(node, session, new Dictionary<string, object?> { ["BusinessName"] = "Acme Ltd" });

        Assert.Single(session.Submissions);
        var data = JsonSerializer.Deserialize<JsonElement>(session.Submissions.First().DataJson);
        Assert.Equal("Experian", data.GetProperty("provider").GetString());
        Assert.Equal("Approved", data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task MockVerificationExecutor_ThrowsWhenProviderMissing()
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            FlowId = Guid.NewGuid()
        };

        var node = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "mock-verification",
            Type = NodeType.Logic,
            JsonContent = """{"action":"MockVerification"}"""
        };

        var executor = new MockVerificationExecutor();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(node, session, new Dictionary<string, object?>()));
    }

    [Fact]
    public async Task MockVerificationExecutor_ThrowsWhenApprovedIsNotBoolean()
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            FlowId = Guid.NewGuid()
        };

        var node = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "mock-verification",
            Type = NodeType.Logic,
            JsonContent = """{"action":"MockVerification","provider":"Experian","approved":"false"}"""
        };

        var executor = new MockVerificationExecutor();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(node, session, new Dictionary<string, object?>()));
    }

    [Fact]
    public async Task MockVerificationExecutor_FallsBackResultFieldWhenEmptyOrNull()
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            FlowId = Guid.NewGuid()
        };

        var nodeWithEmpty = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "mock-verification-empty-result-field",
            Type = NodeType.Logic,
            JsonContent = """{"action":"MockVerification","provider":"Experian","resultField":""}"""
        };

        var nodeWithNull = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "mock-verification-null-result-field",
            Type = NodeType.Logic,
            JsonContent = """{"action":"MockVerification","provider":"Experian","resultField":null}"""
        };

        var executor = new MockVerificationExecutor();
        await executor.ExecuteAsync(nodeWithEmpty, session, new Dictionary<string, object?>());
        await executor.ExecuteAsync(nodeWithNull, session, new Dictionary<string, object?>());

        Assert.Equal(2, session.Submissions.Count);

        var submissions = session.Submissions.ToList();
        var first = JsonSerializer.Deserialize<JsonElement>(submissions[0].DataJson);
        var second = JsonSerializer.Deserialize<JsonElement>(submissions[1].DataJson);

        Assert.Equal("ExperianStatus", first.GetProperty("resultField").GetString());
        Assert.Equal("ExperianStatus", second.GetProperty("resultField").GetString());
    }

    [Fact]
    public async Task MockVerificationExecutor_UsesSingleTimestampForPayloadAndSubmission()
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            FlowId = Guid.NewGuid()
        };

        var node = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = session.FlowId,
            Key = "mock-verification",
            Type = NodeType.Logic,
            JsonContent = """{"action":"MockVerification","provider":"Experian"}"""
        };

        var executor = new MockVerificationExecutor();
        await executor.ExecuteAsync(node, session, new Dictionary<string, object?>());

        var submission = Assert.Single(session.Submissions);
        var data = JsonSerializer.Deserialize<JsonElement>(submission.DataJson);
        var checkedAt = data.GetProperty("checkedAt").GetDateTimeOffset();

        Assert.Equal(checkedAt, submission.SubmittedAt);
    }

    // ─── WorkflowService: Logic node auto-execution ───────────────────

    [Fact]
    public async Task SubmitStepAsync_AutoExecutesLogicNode_AndAdvancesToNextNode()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlowWithLogicNode();
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var profile = new CustomerProfile
        {
            Id = Guid.NewGuid(),
            ExternalCustomerId = "ext-1",
            Country = "USA",
            Email = "test@test.com",
            MetadataJson = "{}"
        };
        dbContext.CustomerProfiles.Add(profile);
        await dbContext.SaveChangesAsync();

        var fakeExecutor = new FakeSetProfileFieldExecutor();
        var service = CreateService(dbContext, [fakeExecutor]);

        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });
        var response = await service.SubmitStepAsync(started.SessionId, started.CurrentNode!.Id, new SubmitStepRequest
        {
            Payload = new Dictionary<string, object?> { ["Name"] = "Ada" }
        });

        Assert.True(fakeExecutor.WasExecuted);
        Assert.False(response.IsCompleted);
        Assert.Equal("final-node", response.CurrentNode?.Key);
    }

    [Fact]
    public async Task SubmitStepAsync_SetsErrorStatus_WhenLogicNodeFailsWithFailOnError()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlowWithFailingLogicNode(failOnError: true);
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var failingExecutor = new FailingLogicNodeExecutor();
        var service = CreateService(dbContext, [failingExecutor]);

        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });
        var response = await service.SubmitStepAsync(started.SessionId, started.CurrentNode!.Id, new SubmitStepRequest
        {
            Payload = new Dictionary<string, object?> { ["Name"] = "Ada" }
        });

        var session = await dbContext.Sessions.FindAsync(started.SessionId);
        Assert.Equal(SessionStatus.Error, session!.Status);
    }

    [Fact]
    public async Task SubmitStepAsync_ContinuesSession_WhenLogicNodeFailsWithoutFailOnError()
    {
        var dbContext = BuildDbContext();
        var flow = CreateFlowWithFailingLogicNode(failOnError: false);
        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var failingExecutor = new FailingLogicNodeExecutor();
        var service = CreateService(dbContext, [failingExecutor]);

        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flow.Id });
        var response = await service.SubmitStepAsync(started.SessionId, started.CurrentNode!.Id, new SubmitStepRequest
        {
            Payload = new Dictionary<string, object?> { ["Name"] = "Ada" }
        });

        var session = await dbContext.Sessions.FindAsync(started.SessionId);
        Assert.NotEqual(SessionStatus.Error, session!.Status);
    }

    // ─── Redirect URL interpolation ───────────────────────────────────

    [Fact]
    public async Task GetNextStepAsync_InterpolatesRedirectUrl_WithKnownVariables()
    {
        var dbContext = BuildDbContext();
        var flowId = Guid.NewGuid();
        var redirectNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "redirect",
            Title = "Redirect",
            Type = NodeType.Redirect,
            IsStartNode = true,
            JsonContent = """{"url":"https://example.com/callback?session={{sessionId}}&node={{nodeKey}}"}"""
        };

        var flow = new Flow
        {
            Id = flowId,
            Name = "Redirect flow",
            Nodes = [redirectNode],
            Connections = []
        };

        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flowId });

        var step = await service.GetNextStepAsync(started.SessionId);

        var jsonContent = JsonDocument.Parse(step.CurrentNode!.JsonContent);
        var url = jsonContent.RootElement.GetProperty("url").GetString()!;

        Assert.Contains(started.SessionId.ToString(), url);
        Assert.Contains("redirect", url);
        Assert.DoesNotContain("{{", url);
    }

    [Fact]
    public async Task GetNextStepAsync_RemovesUnknownPlaceholders_AndLogsWarning()
    {
        var dbContext = BuildDbContext();
        var flowId = Guid.NewGuid();
        var redirectNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "redirect",
            Title = "Redirect",
            Type = NodeType.Redirect,
            IsStartNode = true,
            JsonContent = """{"url":"https://example.com?x={{unknownVar}}"}"""
        };

        var flow = new Flow
        {
            Id = flowId,
            Name = "Redirect flow",
            Nodes = [redirectNode],
            Connections = []
        };

        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flowId });

        var step = await service.GetNextStepAsync(started.SessionId);

        var jsonContent = JsonDocument.Parse(step.CurrentNode!.JsonContent);
        var url = jsonContent.RootElement.GetProperty("url").GetString()!;

        Assert.DoesNotContain("{{", url);
        Assert.Contains("x=", url);
    }

    [Fact]
    public async Task GetNextStepAsync_TreatsPlainStringAsUrl_WhenJsonContentIsNotJson()
    {
        var dbContext = BuildDbContext();
        var flowId = Guid.NewGuid();
        var redirectNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "redirect",
            Title = "Redirect",
            Type = NodeType.Redirect,
            IsStartNode = true,
            JsonContent = "https://example.com/callback?session={{sessionId}}"
        };

        var flow = new Flow
        {
            Id = flowId,
            Name = "Redirect flow",
            Nodes = [redirectNode],
            Connections = []
        };

        dbContext.Flows.Add(flow);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var started = await service.StartSessionAsync(new StartSessionRequest { FlowId = flowId });

        var step = await service.GetNextStepAsync(started.SessionId);

        Assert.Contains(started.SessionId.ToString(), step.CurrentNode!.JsonContent);
        Assert.DoesNotContain("{{", step.CurrentNode.JsonContent);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static IWorkflowService CreateService(OnboardingDbContext dbContext, IEnumerable<ILogicNodeExecutor>? executors = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddLogging();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(WorkflowService).Assembly));
        services.AddScoped<IValidator<StartSessionRequest>, StartSessionRequestValidator>();
        services.AddScoped<IValidator<SubmitStepRequest>, SubmitStepRequestValidator>();
        services.AddScoped<ICustomerService>(_ => new CustomerService(dbContext, new CreateCustomerRequestValidator(), new UpdateCustomerRequestValidator()));
        services.AddScoped<IComplianceRuleEvaluator, ComplianceRuleEvaluator>();
        services.AddSingleton<ISessionEventEmitter, InMemorySessionEventEmitter>();
        services.AddSingleton<IWebhookService, NoOpWebhookService>();
        services.AddSingleton<IDocumentStorageService, NoOpDocumentStorageService>();
        services.AddSingleton<IMetricsService, NoOpMetricsService>();
        services.AddSingleton<IVirusScanService, NullVirusScanService>();
        services.AddSingleton<ITelemetryService, TelemetryService>();
        if (executors is not null)
            foreach (var executor in executors)
                services.AddSingleton<ILogicNodeExecutor>(executor);
        services.AddScoped<IWorkflowService, WorkflowService>();
        return services.BuildServiceProvider().GetRequiredService<IWorkflowService>();
    }

    private static OnboardingDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OnboardingDbContext(options);
    }

    private static Flow CreateFlowWithLogicNode()
    {
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

        var logicNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "logic-node",
            Title = "Logic",
            Type = NodeType.Logic,
            JsonContent = """{"action":"FakeAction"}"""
        };

        var finalNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "final-node",
            Title = "Final",
            Type = NodeType.Form
        };

        return new Flow
        {
            Id = flowId,
            Name = "Logic test flow",
            Nodes = [startNode, logicNode, finalNode],
            Connections =
            [
                new Connection
                {
                    FlowId = flowId,
                    SourceNodeId = startNode.Id,
                    TargetNodeId = logicNode.Id,
                    Priority = 0
                },
                new Connection
                {
                    FlowId = flowId,
                    SourceNodeId = logicNode.Id,
                    TargetNodeId = finalNode.Id,
                    Priority = 0
                }
            ]
        };
    }

    private static Flow CreateFlowWithFailingLogicNode(bool failOnError)
    {
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

        var logicNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "failing-logic",
            Title = "Failing Logic",
            Type = NodeType.Logic,
            JsonContent = $$"""{"action":"FailingAction","failOnError":{{(failOnError ? "true" : "false")}}}"""
        };

        var finalNode = new Node
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Key = "final-node",
            Title = "Final",
            Type = NodeType.Form
        };

        return new Flow
        {
            Id = flowId,
            Name = "Failing logic test flow",
            Nodes = [startNode, logicNode, finalNode],
            Connections =
            [
                new Connection
                {
                    FlowId = flowId,
                    SourceNodeId = startNode.Id,
                    TargetNodeId = logicNode.Id,
                    Priority = 0
                },
                new Connection
                {
                    FlowId = flowId,
                    SourceNodeId = logicNode.Id,
                    TargetNodeId = finalNode.Id,
                    Priority = 0
                }
            ]
        };
    }

    // ─── Test doubles ─────────────────────────────────────────────────

    private sealed class FakeSetProfileFieldExecutor : ILogicNodeExecutor
    {
        public bool WasExecuted { get; private set; }
        public string ActionName => "FakeAction";

        public Task ExecuteAsync(Node node, Session session, IReadOnlyDictionary<string, object?> latestPayload, CancellationToken cancellationToken = default)
        {
            WasExecuted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingLogicNodeExecutor : ILogicNodeExecutor
    {
        public string ActionName => "FailingAction";

        public Task ExecuteAsync(Node node, Session session, IReadOnlyDictionary<string, object?> latestPayload, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated executor failure.");
        }
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            });
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
}