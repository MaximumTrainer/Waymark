using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenOnboarding.Api.Authentication;
using OpenOnboarding.Api.RateLimiting;
using OpenOnboarding.Application.Tests.TestHelpers;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// A rate limit is only a defence if it bounds the caller who is misbehaving. When every caller
/// shares one bucket the limit becomes the attack: 100 requests a minute to <c>session-start</c>
/// stops every applicant from beginning a journey.
/// <para>
/// These run in the Development environment because the Testing environment swaps every limiter for
/// a no-op.
/// </para>
/// </summary>
public sealed class RateLimitPartitioningTests
{
    private const int Limit = 3;

    private const string ClientA = "198.51.100.10";
    private const string ClientB = "198.51.100.20";
    private const string TrustedProxy = "10.1.2.3";
    private const string UntrustedHop = "203.0.113.77";

    private sealed record StartResponse(Guid SessionId, string? ApplicantToken);
    private sealed record FlowStub(Guid Id);

    private static WebApplicationFactory<Program> CreateFactory(
        IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RateLimiting:SessionStartPerMinute"] = Limit.ToString(),
            ["RateLimiting:AnalyticsIngestPerMinute"] = Limit.ToString(),
            ["RateLimiting:WebhookRegistrationPerMinute"] = Limit.ToString(),
            ["RateLimiting:GeneralPerMinute"] = "1000",
            // High enough to stay out of the way of the per-partition assertions; the ceiling gets
            // its own test.
            ["RateLimiting:GlobalCeilingPerMinute"] = "1000"
        };

        if (extraSettings is not null)
        {
            foreach (var setting in extraSettings)
                settings[setting.Key] = setting.Value;
        }

        return TestWebAppFactory.Create(configurationOverrides: settings, environment: "Development");
    }

    private static HttpRequestMessage StartRequest(Guid flowId, string clientAddress, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/workflow/sessions/start")
        {
            Content = JsonContent.Create(new { flowId })
        };
        request.Headers.Add(ClientAddressStartupFilter.HeaderName, clientAddress);

        if (forwardedFor is not null)
            request.Headers.Add("X-Forwarded-For", forwardedFor);

        return request;
    }

    private static async Task<Guid> CreateFlowAsync(HttpClient operatorClient)
    {
        var payload = new
        {
            name = $"Partition flow {Guid.NewGuid()}",
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

    private static async Task ExhaustSessionStartAsync(
        HttpClient client, Guid flowId, string clientAddress, string? forwardedFor = null)
    {
        for (var i = 0; i < Limit; i++)
        {
            var response = await client.SendAsync(StartRequest(flowId, clientAddress, forwardedFor));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        var overTheLimit = await client.SendAsync(StartRequest(flowId, clientAddress, forwardedFor));
        Assert.Equal(HttpStatusCode.TooManyRequests, overTheLimit.StatusCode);
    }

    [Fact]
    public async Task SessionStart_OneClientExhaustingItsBudget_DoesNotLockOutAnother()
    {
        using var factory = CreateFactory();
        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");
        var flowId = await CreateFlowAsync(operatorClient);

        using var client = factory.CreateClient();
        await ExhaustSessionStartAsync(client, flowId, ClientA);

        var otherClient = await client.SendAsync(StartRequest(flowId, ClientB));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherClient.StatusCode);
    }

    [Fact]
    public async Task SessionStart_EachClient_ReceivesItsFullPermitLimit()
    {
        using var factory = CreateFactory();
        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");
        var flowId = await CreateFlowAsync(operatorClient);

        using var client = factory.CreateClient();
        await ExhaustSessionStartAsync(client, flowId, ClientA);

        // The second client starts from a full budget, not from whatever the first one left.
        await ExhaustSessionStartAsync(client, flowId, ClientB);
    }

    [Fact]
    public async Task SessionStart_BehindATrustedProxy_PartitionsByTheForwardedClientAddress()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = TrustedProxy
        });

        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");
        var flowId = await CreateFlowAsync(operatorClient);

        using var client = factory.CreateClient();

        // Every request arrives from the proxy; only X-Forwarded-For tells the two callers apart.
        await ExhaustSessionStartAsync(client, flowId, TrustedProxy, forwardedFor: ClientA);

        var otherClient = await client.SendAsync(StartRequest(flowId, TrustedProxy, forwardedFor: ClientB));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherClient.StatusCode);
    }

    [Fact]
    public async Task SessionStart_ForgedForwardedFor_FromAnUntrustedSource_CannotEvadeTheLimit()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = TrustedProxy
        });

        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");
        var flowId = await CreateFlowAsync(operatorClient);

        using var client = factory.CreateClient();
        await ExhaustSessionStartAsync(client, flowId, UntrustedHop, forwardedFor: ClientA);

        // A fresh header value from the same untrusted connection must not mint a fresh budget.
        var rotated = await client.SendAsync(StartRequest(flowId, UntrustedHop, forwardedFor: ClientB));

        Assert.Equal(HttpStatusCode.TooManyRequests, rotated.StatusCode);
    }

    [Fact]
    public async Task AnalyticsIngest_IsPartitionedBySession_SoOneApplicantCannotThrottleAnother()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:SessionStartPerMinute"] = "1000"
        });

        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var first = await StartSessionAsync(browser, flowId);
        var second = await StartSessionAsync(browser, flowId);

        for (var i = 0; i < Limit; i++)
        {
            var response = await browser.SendAsync(IngestRequest(first, flowId));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var exhausted = await browser.SendAsync(IngestRequest(first, flowId));
        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);

        // The second applicant shares the client address but not the session.
        var otherApplicant = await browser.SendAsync(IngestRequest(second, flowId));
        Assert.Equal(HttpStatusCode.Accepted, otherApplicant.StatusCode);
    }

    [Fact]
    public async Task WebhookRegistration_ExhaustedByOnePrincipal_StillAdmitsADifferentPartition()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < Limit + 1; i++)
            await client.SendAsync(WebhookRequest(ClientA, apiKey: "test-api-key"));

        var limited = await client.SendAsync(WebhookRequest(ClientA, apiKey: "test-api-key"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        // An anonymous caller from another address falls in a different partition, so it reaches
        // authorization and is refused there - not by the exhausted operator limiter.
        var anonymous = await client.SendAsync(WebhookRequest(ClientB, apiKey: null));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, anonymous.StatusCode);
    }

    [Fact]
    public async Task GlobalCeiling_BoundsAFloodSpreadAcrossManyPartitions()
    {
        const int ceiling = 5;

        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            // Per-caller budgets far above the ceiling, so only the ceiling can reject.
            ["RateLimiting:SessionStartPerMinute"] = "1000",
            ["RateLimiting:GlobalCeilingPerMinute"] = ceiling.ToString()
        });

        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < ceiling + 3; i++)
        {
            // A different caller each time: nothing but the global ceiling is shared.
            var response = await client.SendAsync(StartRequest(Guid.NewGuid(), $"198.51.100.{100 + i}"));
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Theory]
    [InlineData("session-start")]
    [InlineData("analytics-ingest")]
    [InlineData("webhook-registration")]
    [InlineData("general")]
    public void EveryPolicy_DerivesADistinctKey_ForTwoDifferentCallers(string policy)
    {
        Func<HttpContext, string> partitioner = policy switch
        {
            "session-start" => RateLimitPartitionKeys.ClientAddress,
            "analytics-ingest" => RateLimitPartitionKeys.ApplicantSession,
            _ => RateLimitPartitionKeys.Principal
        };

        var first = partitioner(CallerContext(ClientA, Guid.NewGuid(), "operator-one"));
        var second = partitioner(CallerContext(ClientB, Guid.NewGuid(), "operator-two"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void PartitionKeys_NeverCollideAcrossKinds()
    {
        // A principal id that reads exactly like an address must not spend the address budget.
        var context = CallerContext(ClientA, sessionId: null, principal: ClientA);

        Assert.NotEqual(RateLimitPartitionKeys.ClientAddress(context), RateLimitPartitionKeys.Principal(context));
    }

    [Fact]
    public void ApplicantSession_WithoutASessionClaim_FallsBackToThePrincipal()
    {
        var context = CallerContext(ClientA, sessionId: null, principal: "operator-one");

        Assert.Equal(
            RateLimitPartitionKeys.Principal(context),
            RateLimitPartitionKeys.ApplicantSession(context));
    }

    [Fact]
    public void Principal_WithoutACredential_FallsBackToTheClientAddress()
    {
        var context = CallerContext(ClientA, sessionId: null, principal: null);

        Assert.Equal(
            RateLimitPartitionKeys.ClientAddress(context),
            RateLimitPartitionKeys.Principal(context));
    }

    private static HttpRequestMessage WebhookRequest(string clientAddress, string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/flows/{Guid.NewGuid()}/webhooks")
        {
            Content = JsonContent.Create(new
            {
                url = "https://example.com/hook",
                secret = "a-secret-long-enough-to-pass"
            })
        };
        request.Headers.Add(ClientAddressStartupFilter.HeaderName, clientAddress);

        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        return request;
    }

    private static HttpContext CallerContext(string address, Guid? sessionId, string? principal)
    {
        var claims = new List<Claim>();

        if (principal is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, principal));

        if (sessionId is not null)
            claims.Add(new Claim(ApplicantSessionAuthenticationDefaults.SessionIdClaim, sessionId.Value.ToString()));

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return context;
    }

    private static async Task<StartResponse> StartSessionAsync(HttpClient client, Guid flowId)
    {
        var response = await client.SendAsync(StartRequest(flowId, ClientA));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartResponse>())!;
    }

    private static HttpRequestMessage IngestRequest(StartResponse session, Guid flowId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/analytics/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.ApplicantToken);
        request.Headers.Add(ClientAddressStartupFilter.HeaderName, ClientA);
        request.Content = JsonContent.Create(new
        {
            events = new[]
            {
                new
                {
                    eventType = "step_view",
                    journeyId = flowId.ToString(),
                    sessionId = session.SessionId.ToString()
                }
            }
        });
        return request;
    }
}
