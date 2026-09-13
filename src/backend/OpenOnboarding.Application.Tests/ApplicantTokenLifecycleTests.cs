using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenOnboarding.Api.Authentication;
using OpenOnboarding.Api.Authorization;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// The applicant token is issued once at session start and nothing renews or withdraws it. Two
/// things follow: a long journey outlives its own credential and strands the applicant with no way
/// forward but to start again, and a finished journey leaves a working credential behind for up to
/// a day - on a shared machine, well past the visit that created it.
/// </summary>
public sealed class ApplicantTokenLifecycleTests
{
    private const string OperatorApiKey = "test-api-key";

    /// <summary>
    /// Fixed so a test can mint its own token - an expired one in particular, which is otherwise
    /// only reachable by waiting out the lifetime.
    /// </summary>
    private const string SigningKey = "applicant-token-lifecycle-tests-signing-key-long-enough-for-hmac-sha256";

    private sealed record StepResponse(
        Guid SessionId,
        bool IsCompleted,
        NodeStub? CurrentNode,
        string? ApplicantToken,
        DateTimeOffset? ApplicantTokenExpiresAt);

    private sealed record NodeStub(Guid Id, string Key, string Type);

    private sealed record FlowStub(Guid Id);

    private static WebApplicationFactory<Program> CreateFactory()
        => TestWebAppFactory.Create(configurationOverrides: new Dictionary<string, string?>
        {
            ["Authentication:ApplicantToken:SigningKey"] = SigningKey
        });

    private static HttpClient CreateOperatorClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", OperatorApiKey);
        return client;
    }

    /// <summary>A one-step flow: submitting its only step completes the session.</summary>
    private static async Task<Guid> CreateSingleStepFlowAsync(HttpClient operatorClient)
    {
        var response = await operatorClient.PostAsJsonAsync("/api/flows", new
        {
            name = $"Token lifecycle flow {Guid.NewGuid()}",
            nodes = new[]
            {
                new { key = "start", type = "Form", title = "Start", isStartNode = true, jsonContent = "{\"fields\":[]}" }
            },
            connections = Array.Empty<object>()
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FlowStub>())!.Id;
    }

    /// <summary>A two-step flow, so a journey can continue on the credential the first step returned.</summary>
    private static async Task<Guid> CreateTwoStepFlowAsync(HttpClient operatorClient)
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var response = await operatorClient.PostAsJsonAsync("/api/flows", new
        {
            name = $"Token lifecycle flow {Guid.NewGuid()}",
            nodes = new object[]
            {
                new { id = first, key = "start", type = "Form", title = "Start", isStartNode = true, jsonContent = "{\"fields\":[]}" },
                new { id = second, key = "details", type = "Form", title = "Details", isStartNode = false, jsonContent = "{\"fields\":[]}" }
            },
            connections = new object[]
            {
                new { sourceNodeId = first, targetNodeId = second, priority = 0 }
            }
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FlowStub>())!.Id;
    }

    private static async Task<StepResponse> StartSessionAsync(HttpClient browser, Guid flowId)
    {
        var response = await browser.PostAsJsonAsync("/api/workflow/sessions/start", new { flowId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StepResponse>())!;
    }

    private static HttpRequestMessage SubmitRequest(Guid sessionId, Guid nodeId, string token)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/workflow/sessions/{sessionId}/steps/{nodeId}/submit")
        {
            Content = JsonContent.Create(new { payload = new Dictionary<string, object>() })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage IngestRequest(Guid sessionId, Guid flowId, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/analytics/events")
        {
            Content = JsonContent.Create(new
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
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage ReadRequest(Guid sessionId, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/workflow/sessions/{sessionId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>
    /// Mints a token for the session that expired in the past. Signed with the same key the API
    /// uses, so the only reason it can be refused is its lifetime.
    /// </summary>
    private static string ExpiredTokenFor(Guid sessionId)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = ApplicantSessionAuthenticationDefaults.Issuer,
            Audience = ApplicantSessionAuthenticationDefaults.Audience,
            IssuedAt = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddHours(-1),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = sessionId.ToString(),
                [ApplicantSessionAuthenticationDefaults.SessionIdClaim] = sessionId.ToString(),
                [ClaimTypes.Role] = AppRoles.Applicant
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    // ── Renewal ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmittingAStep_ReturnsARefreshedToken()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateTwoStepFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var start = await StartSessionAsync(browser, flowId);

        var response = await browser.SendAsync(
            SubmitRequest(start.SessionId, start.CurrentNode!.Id, start.ApplicantToken!));
        response.EnsureSuccessStatusCode();

        var submitted = (await response.Content.ReadFromJsonAsync<StepResponse>())!;

        Assert.False(string.IsNullOrWhiteSpace(submitted.ApplicantToken));
        Assert.True(submitted.ApplicantTokenExpiresAt > DateTimeOffset.UtcNow);

        // Not asserted to differ from the original: a JWT carries its expiry to the second, so two
        // issuances inside the same second are byte-identical. What matters is that the response
        // carries a usable credential for this session - see AJourneyContinues_OnTheRefreshedTokenAlone.
        var claims = new JsonWebTokenHandler().ReadJsonWebToken(submitted.ApplicantToken);
        Assert.Equal(
            start.SessionId.ToString(),
            claims.GetClaim(ApplicantSessionAuthenticationDefaults.SessionIdClaim).Value);
    }

    [Fact]
    public async Task TheRefreshedToken_ExpiresLaterThanTheOneItReplaces()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateTwoStepFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var start = await StartSessionAsync(browser, flowId);

        var response = await browser.SendAsync(
            SubmitRequest(start.SessionId, start.CurrentNode!.Id, start.ApplicantToken!));
        var submitted = (await response.Content.ReadFromJsonAsync<StepResponse>())!;

        // This is what keeps a journey alive: each step pushes the deadline out, so the session
        // cannot outlive its own credential while it is still being worked on.
        Assert.NotNull(submitted.ApplicantTokenExpiresAt);
        Assert.True(
            submitted.ApplicantTokenExpiresAt > start.ApplicantTokenExpiresAt,
            "the refreshed token must expire later than the one it replaces");
    }

    [Fact]
    public async Task AJourneyContinues_OnTheRefreshedTokenAlone()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateTwoStepFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var start = await StartSessionAsync(browser, flowId);

        var first = await browser.SendAsync(
            SubmitRequest(start.SessionId, start.CurrentNode!.Id, start.ApplicantToken!));
        var afterFirst = (await first.Content.ReadFromJsonAsync<StepResponse>())!;

        // The original credential is never used again - the journey finishes on the renewal.
        var second = await browser.SendAsync(
            SubmitRequest(afterFirst.SessionId, afterFirst.CurrentNode!.Id, afterFirst.ApplicantToken!));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var afterSecond = (await second.Content.ReadFromJsonAsync<StepResponse>())!;
        Assert.True(afterSecond.IsCompleted);
    }

    [Fact]
    public async Task AnOperatorSubmitting_IsNotHandedAnApplicantToken()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateTwoStepFlowAsync(operatorClient);

        var start = await StartSessionAsync(operatorClient, flowId);

        var response = await operatorClient.PostAsJsonAsync(
            $"/api/workflow/sessions/{start.SessionId}/steps/{start.CurrentNode!.Id}/submit",
            new { payload = new Dictionary<string, object>() });
        var submitted = (await response.Content.ReadFromJsonAsync<StepResponse>())!;

        // An operator credential already covers the session; minting a second one would be a
        // credential nobody asked for.
        Assert.Null(submitted.ApplicantToken);
        Assert.Null(submitted.ApplicantTokenExpiresAt);
    }

    // ── Expiry ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnExpiredToken_IsRefused()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateTwoStepFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var start = await StartSessionAsync(browser, flowId);

        var response = await browser.SendAsync(
            SubmitRequest(start.SessionId, start.CurrentNode!.Id, ExpiredTokenFor(start.SessionId)));

        // 401, not 403: the credential is not bad, it is over. The frontend tells the two apart to
        // show "your session has expired" rather than a generic failure.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Terminal sessions ──────────────────────────────────────────────────────

    [Fact]
    public async Task ATokenForACompletedSession_IsRefusedFromStepSubmission()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateSingleStepFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var start = await StartSessionAsync(browser, flowId);

        var completing = await browser.SendAsync(
            SubmitRequest(start.SessionId, start.CurrentNode!.Id, start.ApplicantToken!));
        var completed = (await completing.Content.ReadFromJsonAsync<StepResponse>())!;
        Assert.True(completed.IsCompleted);

        var again = await browser.SendAsync(
            SubmitRequest(start.SessionId, start.CurrentNode.Id, completed.ApplicantToken ?? start.ApplicantToken!));

        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
    }

    [Fact]
    public async Task ATokenForACompletedSession_IsRefusedFromAnalyticsIngest()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateSingleStepFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var start = await StartSessionAsync(browser, flowId);

        // Accepted while the journey is live.
        var whileLive = await browser.SendAsync(IngestRequest(start.SessionId, flowId, start.ApplicantToken!));
        Assert.Equal(HttpStatusCode.Accepted, whileLive.StatusCode);

        var completing = await browser.SendAsync(
            SubmitRequest(start.SessionId, start.CurrentNode!.Id, start.ApplicantToken!));
        var completed = (await completing.Content.ReadFromJsonAsync<StepResponse>())!;

        var afterCompletion = await browser.SendAsync(
            IngestRequest(start.SessionId, flowId, completed.ApplicantToken ?? start.ApplicantToken!));

        Assert.Equal(HttpStatusCode.Forbidden, afterCompletion.StatusCode);
    }

    [Fact]
    public async Task ATokenForAnAbandonedSession_IsRefusedFromAnalyticsIngest()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateTwoStepFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var start = await StartSessionAsync(browser, flowId);

        var abandon = new HttpRequestMessage(HttpMethod.Delete, $"/api/workflow/sessions/{start.SessionId}");
        abandon.Headers.Authorization = new AuthenticationHeaderValue("Bearer", start.ApplicantToken!);
        (await browser.SendAsync(abandon)).EnsureSuccessStatusCode();

        var response = await browser.SendAsync(IngestRequest(start.SessionId, flowId, start.ApplicantToken!));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ATokenForACompletedSession_StillServesTheCompletionScreenRead()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateSingleStepFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var start = await StartSessionAsync(browser, flowId);

        var completing = await browser.SendAsync(
            SubmitRequest(start.SessionId, start.CurrentNode!.Id, start.ApplicantToken!));
        var completed = (await completing.Content.ReadFromJsonAsync<StepResponse>())!;

        // The completion screen renders after the session is terminal; refusing this read would
        // break the last thing the applicant sees.
        var read = await browser.SendAsync(
            ReadRequest(start.SessionId, completed.ApplicantToken ?? start.ApplicantToken!));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task AnOperator_StillReachesACompletedSession()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateSingleStepFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var start = await StartSessionAsync(browser, flowId);
        await browser.SendAsync(SubmitRequest(start.SessionId, start.CurrentNode!.Id, start.ApplicantToken!));

        // The terminal-status rule bounds a stale applicant credential; it says nothing about who
        // may act on a finished session.
        var read = await operatorClient.GetAsync($"/api/workflow/sessions/{start.SessionId}");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }
}
