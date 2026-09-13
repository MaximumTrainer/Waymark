using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// Analytics ingest is the one endpoint a browser calls in a loop, so an unbounded one would let a
/// single page fill the event table. These run in the Development environment because the Testing
/// environment deliberately swaps every limiter for a no-op.
/// </summary>
public sealed class AnalyticsRateLimitTests
{
    private const int Limit = 3;

    private sealed record StartResponse(Guid SessionId, string? ApplicantToken);
    private sealed record FlowStub(Guid Id);

    private static WebApplicationFactory<Program> CreateRateLimitedFactory()
        => TestWebAppFactory.Create(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["RateLimiting:AnalyticsIngestPerMinute"] = Limit.ToString(),
                // Keep the other limiters out of the way of the fixture setup.
                ["RateLimiting:SessionStartPerMinute"] = "1000",
                ["RateLimiting:GeneralPerMinute"] = "1000"
            },
            environment: "Development");

    private static async Task<Guid> CreateFlowAsync(HttpClient operatorClient)
    {
        var payload = new
        {
            name = $"Rate limit flow {Guid.NewGuid()}",
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

    private static HttpRequestMessage IngestRequest(Guid sessionId, Guid flowId, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/analytics/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            events = new[]
            {
                new
                {
                    eventType = "step_view",
                    journeyId = flowId.ToString(),
                    sessionId = sessionId.ToString()
                }
            }
        });
        return request;
    }

    [Fact]
    public async Task AnalyticsIngest_BeyondTheConfiguredLimit_Returns429()
    {
        using var factory = CreateRateLimitedFactory();

        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var startResponse = await browser.PostAsJsonAsync("/api/workflow/sessions/start", new { flowId });
        startResponse.EnsureSuccessStatusCode();
        var start = (await startResponse.Content.ReadFromJsonAsync<StartResponse>())!;

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < Limit + 2; i++)
        {
            var response = await browser.SendAsync(IngestRequest(start.SessionId, flowId, start.ApplicantToken!));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(Limit, statuses.Count(s => s == HttpStatusCode.Accepted));
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task AnalyticsIngest_RateLimitedResponse_TellsTheCallerWhenToRetry()
    {
        using var factory = CreateRateLimitedFactory();

        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var startResponse = await browser.PostAsJsonAsync("/api/workflow/sessions/start", new { flowId });
        var start = (await startResponse.Content.ReadFromJsonAsync<StartResponse>())!;

        HttpResponseMessage? limited = null;
        for (var i = 0; i < Limit + 2 && limited is null; i++)
        {
            var response = await browser.SendAsync(IngestRequest(start.SessionId, flowId, start.ApplicantToken!));
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                limited = response;
        }

        Assert.NotNull(limited);
        Assert.True(limited!.Headers.Contains("Retry-After"));
    }
}
