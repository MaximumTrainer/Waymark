using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// The public onboarding app must complete a journey without ever holding an operator credential,
/// and the credential it does hold must reach exactly one session and no operator data.
/// </summary>
public sealed class ApplicantSessionTokenTests
{
    private const string OperatorApiKey = "test-api-key";

    private sealed record StartResponse(
        Guid SessionId,
        bool IsCompleted,
        NodeStub? CurrentNode,
        string? ApplicantToken,
        DateTimeOffset? ApplicantTokenExpiresAt);

    private sealed record NodeStub(Guid Id, string Key, string Type);

    private sealed record FlowStub(Guid Id);

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
            name = $"Applicant token flow {Guid.NewGuid()}",
            nodes = new[]
            {
                new { key = "start", type = "Form", title = "Start", isStartNode = true, jsonContent = "{\"fields\":[]}" }
            },
            connections = Array.Empty<object>()
        };

        var response = await operatorClient.PostAsJsonAsync("/api/flows", payload);
        response.EnsureSuccessStatusCode();
        var flow = await response.Content.ReadFromJsonAsync<FlowStub>();
        return flow!.Id;
    }

    /// <summary>Starts a session the way a browser does: no credential of any kind.</summary>
    private static async Task<StartResponse> StartAnonymousSessionAsync(HttpClient client, Guid flowId)
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

    // =======================================================================
    // The journey works with no operator credential in the browser
    // =======================================================================
    [Fact]
    public async Task StartSession_WithoutAnyCredential_SucceedsAndReturnsApplicantToken()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/workflow/sessions/start")
        {
            Content = JsonContent.Create(new { flowId })
        };
        var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(request.Headers.Contains("X-Api-Key"));

        var start = await response.Content.ReadFromJsonAsync<StartResponse>();
        Assert.NotNull(start);
        Assert.NotEqual(Guid.Empty, start!.SessionId);
        Assert.False(string.IsNullOrWhiteSpace(start.ApplicantToken));
        Assert.True(start.ApplicantTokenExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ApplicantToken_CanSubmitAStepForItsOwnSession()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartAnonymousSessionAsync(browser, flowId);

        var submit = WithToken(
            HttpMethod.Post,
            $"/api/workflow/sessions/{start.SessionId}/steps/{start.CurrentNode!.Id}/submit",
            start.ApplicantToken!);
        submit.Content = JsonContent.Create(new { payload = new Dictionary<string, object>() });

        var response = await browser.SendAsync(submit);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApplicantToken_CanReadItsOwnSession()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartAnonymousSessionAsync(browser, flowId);

        var response = await browser.SendAsync(
            WithToken(HttpMethod.Get, $"/api/workflow/sessions/{start.SessionId}", start.ApplicantToken!));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =======================================================================
    // A token for session A must not reach session B
    // =======================================================================
    [Fact]
    public async Task ApplicantToken_IsRejectedForAnotherSession()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var sessionA = await StartAnonymousSessionAsync(browser, flowId);
        var sessionB = await StartAnonymousSessionAsync(browser, flowId);

        // Read
        var read = await browser.SendAsync(
            WithToken(HttpMethod.Get, $"/api/workflow/sessions/{sessionB.SessionId}", sessionA.ApplicantToken!));
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        // Submit
        var submit = WithToken(
            HttpMethod.Post,
            $"/api/workflow/sessions/{sessionB.SessionId}/steps/{sessionB.CurrentNode!.Id}/submit",
            sessionA.ApplicantToken!);
        submit.Content = JsonContent.Create(new { payload = new Dictionary<string, object>() });
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(submit)).StatusCode);

        // Next step
        var next = await browser.SendAsync(
            WithToken(HttpMethod.Get, $"/api/workflow/sessions/{sessionB.SessionId}/next", sessionA.ApplicantToken!));
        Assert.Equal(HttpStatusCode.Forbidden, next.StatusCode);

        // Abandon
        var abandon = await browser.SendAsync(
            WithToken(HttpMethod.Delete, $"/api/workflow/sessions/{sessionB.SessionId}", sessionA.ApplicantToken!));
        Assert.Equal(HttpStatusCode.Forbidden, abandon.StatusCode);
    }

    [Fact]
    public async Task ApplicantToken_IsRejectedForAnotherSessionsEventStream()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var sessionA = await StartAnonymousSessionAsync(browser, flowId);
        var sessionB = await StartAnonymousSessionAsync(browser, flowId);

        // EventSource cannot set headers, so the SSE endpoint also accepts the token as a query
        // parameter - it must be scoped just as tightly.
        var response = await browser.GetAsync(
            $"/api/workflow/sessions/{sessionB.SessionId}/events?access_token={sessionA.ApplicantToken}",
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =======================================================================
    // A token must reach no operator data at all
    // =======================================================================
    /// <summary>Operator-only reads. <c>{flowId}</c> is substituted with a real flow.</summary>
    public static TheoryData<string> OperatorOnlyEndpoints => new()
    {
        "/api/workflow/sessions",
        "/api/flows",
        "/api/flows/{flowId}/webhooks",
        "/api/flows/{flowId}/webhook-deliveries",
        "/api/customers"
    };

    [Theory]
    [MemberData(nameof(OperatorOnlyEndpoints))]
    public async Task ApplicantToken_IsRejectedFromOperatorOnlyEndpoints(string url)
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartAnonymousSessionAsync(browser, flowId);

        var resolvedUrl = url.Replace("{flowId}", flowId.ToString());
        var response = await browser.SendAsync(WithToken(HttpMethod.Get, resolvedUrl, start.ApplicantToken!));

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"{resolvedUrl} returned {(int)response.StatusCode} for an applicant token; expected 401 or 403.");
    }

    [Fact]
    public async Task ApplicantToken_IsRejectedFromSubmissionsAndFlowStats()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartAnonymousSessionAsync(browser, flowId);

        // Submissions for the applicant's own session are still operator-only: the PII readout is
        // an operator view, not part of completing the journey.
        var submissions = await browser.SendAsync(WithToken(
            HttpMethod.Get, $"/api/workflow/sessions/{start.SessionId}/submissions", start.ApplicantToken!));
        Assert.Equal(HttpStatusCode.Forbidden, submissions.StatusCode);

        var stats = await browser.SendAsync(WithToken(
            HttpMethod.Get, $"/api/workflow/flows/{flowId}/stats", start.ApplicantToken!));
        Assert.Equal(HttpStatusCode.Forbidden, stats.StatusCode);
    }

    // =======================================================================
    // Token integrity
    // =======================================================================
    [Fact]
    public async Task TamperedApplicantToken_IsRejected()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        using var browser = CreateClient(factory);
        var start = await StartAnonymousSessionAsync(browser, flowId);

        // Flip the last character of the signature.
        var token = start.ApplicantToken!;
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        var response = await browser.SendAsync(
            WithToken(HttpMethod.Get, $"/api/workflow/sessions/{start.SessionId}", tampered));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OperatorStartedSession_DoesNotMintAnApplicantToken()
    {
        using var factory = TestWebAppFactory.Create();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateFlowAsync(operatorClient);

        var response = await operatorClient.PostAsJsonAsync("/api/workflow/sessions/start", new { flowId });
        response.EnsureSuccessStatusCode();

        var start = await response.Content.ReadFromJsonAsync<StartResponse>();
        Assert.Null(start!.ApplicantToken);
    }
}
