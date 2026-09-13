using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Infrastructure.DependencyInjection;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// Analytics events must survive past the log: a completed journey should leave a readable,
/// ordered trail, and a browser must be able to add to it without being able to forge events
/// against another session.
/// </summary>
public sealed class AnalyticsIngestionTests
{
    private const string OperatorApiKey = "test-api-key";

    private sealed record StartResponse(Guid SessionId, NodeStub? CurrentNode, string? ApplicantToken);
    private sealed record NodeStub(Guid Id);
    private sealed record FlowStub(Guid Id);
    private sealed record TrailEvent(string EventType, string SessionId, string Source, DateTimeOffset OccurredAt);

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static HttpClient CreateOperatorClient(WebApplicationFactory<Program> factory)
    {
        var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", OperatorApiKey);
        return client;
    }

    private static async Task<Guid> CreateFlowAsync(HttpClient operatorClient)
    {
        var payload = new
        {
            name = $"Analytics flow {Guid.NewGuid()}",
            nodes = new[]
            {
                new { key = "start", type = "Form", title = "Start", isStartNode = true, jsonContent = "{\"fields\":[]}" }
            },
            connections = Array.Empty<object>()
        };

        var response = await operatorClient.PostAsJsonAsync("/api/flows", payload);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FlowStub>())!.Id;
    }

    private static async Task<StartResponse> StartSessionAsync(HttpClient client, Guid flowId)
    {
        var response = await client.PostAsJsonAsync("/api/workflow/sessions/start", new { flowId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartResponse>())!;
    }

    private static HttpRequestMessage WithToken(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static object IngestBatch(Guid sessionId, Guid flowId, params string[] eventTypes) => new
    {
        events = eventTypes.Select((eventType, index) => new
        {
            eventId = Guid.NewGuid(),
            eventType,
            journeyId = flowId.ToString(),
            sessionId = sessionId.ToString(),
            stepIndex = index,
            payload = new Dictionary<string, object?> { ["index"] = index },
            occurredAt = DateTimeOffset.UtcNow.AddSeconds(index)
        }).ToArray()
    };

    // =======================================================================
    // Server-raised events are persisted, not just logged
    // =======================================================================
    [Fact]
    public async Task CompletingAJourney_LeavesAnOrderedTrail()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartSessionAsync(browser, flowId);

        var submit = WithToken(
            HttpMethod.Post,
            $"/api/workflow/sessions/{start.SessionId}/steps/{start.CurrentNode!.Id}/submit",
            start.ApplicantToken!);
        submit.Content = JsonContent.Create(new { payload = new Dictionary<string, object>() });
        (await browser.SendAsync(submit)).EnsureSuccessStatusCode();

        var trail = await operatorClient.GetFromJsonAsync<List<TrailEvent>>(
            $"/api/analytics/sessions/{start.SessionId}/events");

        Assert.NotNull(trail);
        Assert.NotEmpty(trail!);
        Assert.All(trail, e => Assert.Equal(start.SessionId.ToString(), e.SessionId));
        Assert.All(trail, e => Assert.Equal(AnalyticsEventSources.Server, e.Source));
        Assert.Equal(trail.OrderBy(e => e.OccurredAt).Select(e => e.EventType), trail.Select(e => e.EventType));
    }

    // =======================================================================
    // Client ingestion
    // =======================================================================
    [Fact]
    public async Task ClientBatch_IsAcceptedAndJoinsTheTrailMarkedAsClient()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartSessionAsync(browser, flowId);

        var ingest = WithToken(HttpMethod.Post, "/api/analytics/events", start.ApplicantToken!);
        ingest.Content = JsonContent.Create(IngestBatch(start.SessionId, flowId, "step_viewed", "field_focused"));

        var response = await browser.SendAsync(ingest);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var trail = await operatorClient.GetFromJsonAsync<List<TrailEvent>>(
            $"/api/analytics/sessions/{start.SessionId}/events");

        var clientEvents = trail!.Where(e => e.Source == AnalyticsEventSources.Client).ToList();
        Assert.Equal(2, clientEvents.Count);
        Assert.Contains(clientEvents, e => e.EventType == "step_viewed");
        Assert.Contains(clientEvents, e => e.EventType == "field_focused");
    }

    [Fact]
    public async Task ClientBatch_ForAnotherSession_IsForbidden()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var sessionA = await StartSessionAsync(browser, flowId);
        var sessionB = await StartSessionAsync(browser, flowId);

        // Session A's credential, session B's events.
        var ingest = WithToken(HttpMethod.Post, "/api/analytics/events", sessionA.ApplicantToken!);
        ingest.Content = JsonContent.Create(IngestBatch(sessionB.SessionId, flowId, "step_viewed"));

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(ingest)).StatusCode);
    }

    [Fact]
    public async Task ClientBatch_MixingInAnotherSession_IsRejectedEntirely()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var sessionA = await StartSessionAsync(browser, flowId);
        var sessionB = await StartSessionAsync(browser, flowId);

        var mixed = new
        {
            events = new[]
            {
                new { eventType = "step_viewed", journeyId = flowId.ToString(), sessionId = sessionA.SessionId.ToString() },
                new { eventType = "step_viewed", journeyId = flowId.ToString(), sessionId = sessionB.SessionId.ToString() }
            }
        };

        var ingest = WithToken(HttpMethod.Post, "/api/analytics/events", sessionA.ApplicantToken!);
        ingest.Content = JsonContent.Create(mixed);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(ingest)).StatusCode);

        // The valid half must not have been written either.
        var trail = await operatorClient.GetFromJsonAsync<List<TrailEvent>>(
            $"/api/analytics/sessions/{sessionA.SessionId}/events");
        Assert.DoesNotContain(trail!, e => e.Source == AnalyticsEventSources.Client);
    }

    [Fact]
    public async Task ClientBatch_WithoutAnyCredential_IsRejected()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartSessionAsync(browser, flowId);

        var response = await browser.PostAsJsonAsync(
            "/api/analytics/events", IngestBatch(start.SessionId, flowId, "step_viewed"));

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ClientBatch_OverTheSizeLimit_IsRejected()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartSessionAsync(browser, flowId);

        var tooMany = Enumerable.Repeat("step_viewed", 101).ToArray();
        var ingest = WithToken(HttpMethod.Post, "/api/analytics/events", start.ApplicantToken!);
        ingest.Content = JsonContent.Create(IngestBatch(start.SessionId, flowId, tooMany));

        Assert.Equal(HttpStatusCode.BadRequest, (await browser.SendAsync(ingest)).StatusCode);
    }

    [Fact]
    public async Task ClientBatch_MissingRequiredFields_IsRejected()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartSessionAsync(browser, flowId);

        var ingest = WithToken(HttpMethod.Post, "/api/analytics/events", start.ApplicantToken!);
        ingest.Content = JsonContent.Create(new
        {
            events = new[] { new { eventType = "", journeyId = "", sessionId = start.SessionId.ToString() } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, (await browser.SendAsync(ingest)).StatusCode);
    }

    // =======================================================================
    // The trail is operator-only
    // =======================================================================
    [Fact]
    public async Task SessionTrail_IsNotReadableWithAnApplicantToken()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartSessionAsync(browser, flowId);

        var response = await browser.SendAsync(WithToken(
            HttpMethod.Get, $"/api/analytics/sessions/{start.SessionId}/events", start.ApplicantToken!));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =======================================================================
    // Flow dashboard endpoint the operator analytics view calls
    // =======================================================================
    [Fact]
    public async Task FlowAnalytics_ReturnsTheShapeTheOperatorViewExpects()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        await StartSessionAsync(browser, flowId);

        var response = await operatorClient.GetAsync($"/api/analytics/flows/{flowId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<FlowAnalyticsDto>();
        Assert.NotNull(dto);
        Assert.Equal(flowId, dto!.FlowId);
        Assert.False(string.IsNullOrWhiteSpace(dto.FlowName));
        Assert.Equal(1, dto.TotalSessions);
        Assert.Equal(0, dto.CompletionRate);
    }

    // =======================================================================
    // Provider registration is configuration-driven
    // =======================================================================
    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:OnboardingDb"] = "Host=localhost;Database=open_onboarding;Username=postgres;Password=postgres",
            ["VirusScan:Enabled"] = "false"
        };

        foreach (var entry in overrides ?? [])
            values[entry.Key] = entry.Value;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void DatabaseAnalyticsProvider_IsRegisteredByDefault_WithItsRetentionSweep()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration());

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IAnalyticsProvider) &&
            d.ImplementationType == typeof(DatabaseAnalyticsProvider));

        Assert.Contains(services, d => d.ImplementationType == typeof(CleanupExpiredAnalyticsEventsService));
    }

    [Fact]
    public void DatabaseAnalyticsProvider_CanBeDisabled_LeavingTheConsoleProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Analytics:DatabaseProvider:Enabled"] = "false"
        }));

        Assert.DoesNotContain(services, d =>
            d.ServiceType == typeof(IAnalyticsProvider) &&
            d.ImplementationType == typeof(DatabaseAnalyticsProvider));

        // Nothing to prune, so no sweep either.
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(CleanupExpiredAnalyticsEventsService));

        // The app still has a working analytics pipeline.
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IAnalyticsProvider) &&
            d.ImplementationType == typeof(ConsoleAnalyticsProvider));
    }
}
