using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;

namespace OpenOnboarding.Pact.Tests.E2E;

public sealed class WorkflowE2ETests
{
    private static readonly Guid FlowId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StartNodeId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Journey_ProgressesToCompletion_AndValidatesWebhookSignature()
    {
        const string webhookSecret = "e2e-secret-signature";
        using var handler = new RecordingWebhookHandler([HttpStatusCode.OK]);
        await using var factory = CreateFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        await ClearWebhooksAsync(client);
        await RegisterWebhookAsync(client, webhookSecret);

        var started = await StartSessionAsync(client);
        Assert.True(started.CurrentNode is not null, "[UI] Journey start did not return a renderable node.");
        Assert.True(started.CurrentNode!.Id == StartNodeId, "[UI] Journey start did not resolve the expected start node.");

        var highValueStep = await SubmitStepAsync(client, started.SessionId, StartNodeId, new SubmitStepRequest(
            new Dictionary<string, object?> { ["CompanyName"] = "Waymark Ltd", ["Country"] = "USA", ["AnnualRevenue"] = 2_000_000 }), "UI/API");

        Assert.True(!highValueStep.IsCompleted, "[API] Conditional branch response unexpectedly completed the session.");
        Assert.True(highValueStep.CurrentNode is not null, "[API] Conditional branch did not return the next node.");
        Assert.True(highValueStep.CurrentNode!.Key == "high-value-kyc", "[API] Conditional branch did not route to high-value-kyc.");

        var completed = await SubmitStepAsync(client, started.SessionId, highValueStep.CurrentNode.Id, new SubmitStepRequest(
            new Dictionary<string, object?> { ["SourceOfFunds"] = "Savings" }), "API");

        Assert.True(completed.IsCompleted, "[API] Final submission did not complete the session.");
        Assert.True(completed.CurrentNode is null, "[API] Completed session still returned a current node.");

        await WaitForConditionAsync(() => handler.Attempts.Count >= 1, TimeSpan.FromSeconds(12), "[Integration callback] Webhook callback was not observed in time.");
        var deliveries = await WaitForDeliveryAsync(client, started.SessionId, TimeSpan.FromSeconds(12));
        var delivery = deliveries.FirstOrDefault(d => d.SessionId == started.SessionId && d.EventType == "session-completed");
        Assert.True(delivery is not null, "[Integration callback] Delivery log did not contain the completed-session webhook.");
        Assert.True(delivery!.Status == "delivered", "[Integration callback] Webhook was not marked as delivered.");
        Assert.True(delivery.AttemptCount == 1, "[Integration callback] Expected a single webhook attempt.");

        Assert.True(handler.Attempts.Count == 1, "[Integration callback] Expected one callback attempt for successful webhook delivery.");
        var callback = handler.Attempts[0];
        Assert.True(callback.Signature is not null, "[Integration callback] Webhook signature header was not present.");

        var expectedSignature = ComputeSignature(callback.Body, webhookSecret);
        Assert.True(callback.Signature == $"sha256={expectedSignature}", "[Integration callback] Webhook signature did not match expected HMAC.");

        using var payloadJson = JsonDocument.Parse(callback.Body);
        Assert.True(payloadJson.RootElement.GetProperty("sessionId").GetGuid() == started.SessionId, "[Integration callback] Webhook payload sessionId did not match the completed session.");
    }

    [Fact]
    public async Task Journey_RetriesWebhookFailures_AndPersistsDeliveryLog()
    {
        const string webhookSecret = "e2e-secret-retry";
        using var handler = new RecordingWebhookHandler([HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK]);
        await using var factory = CreateFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        await ClearWebhooksAsync(client);
        await RegisterWebhookAsync(client, webhookSecret);

        var started = await StartSessionAsync(client);
        var highValueStep = await SubmitStepAsync(client, started.SessionId, StartNodeId, new SubmitStepRequest(
            new Dictionary<string, object?> { ["CompanyName"] = "Retry Inc", ["Country"] = "USA", ["AnnualRevenue"] = 1_500_000 }), "UI/API");

        var completed = await SubmitStepAsync(client, started.SessionId, highValueStep.CurrentNode!.Id, new SubmitStepRequest(
            new Dictionary<string, object?> { ["SourceOfFunds"] = "Business revenue" }), "API");
        Assert.True(completed.IsCompleted, "[API] Retry scenario did not complete the session.");

        await WaitForConditionAsync(() => handler.Attempts.Count >= 3, TimeSpan.FromSeconds(16), "[Integration callback] Retry callbacks were not observed in time.");
        Assert.True(handler.Attempts.Count == 3, $"[Integration callback] Expected 3 callback attempts for retry flow, got {handler.Attempts.Count}.");

        var deliveries = await WaitForDeliveryAsync(client, started.SessionId, TimeSpan.FromSeconds(16));
        var delivery = deliveries.FirstOrDefault(d => d.SessionId == started.SessionId && d.EventType == "session-completed");
        Assert.True(delivery is not null, "[Integration callback] Retry scenario did not persist a webhook delivery log.");
        Assert.True(delivery!.Status == "delivered", "[Integration callback] Retry scenario did not end in delivered status.");
        Assert.True(delivery.AttemptCount == 3, "[Integration callback] Delivery log attempt count did not reflect retries.");
        Assert.True(delivery.LastStatusCode == 200, "[Integration callback] Final webhook status code was not persisted as success.");
    }

    private static WebApplicationFactory<Program> CreateFactory(RecordingWebhookHandler handler)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OnboardingDb")
            ?? "Host=localhost;Port=5432;Database=onboarding_test;Username=postgres;Password=postgres";

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:OnboardingDb"] = connectionString,
                    ["Authentication:ApiKey"] = "test-api-key",
                    ["Authentication:JwtAuthority"] = "",
                    ["SessionTimeoutMinutes"] = "1440",
                    ["DocumentUpload:MaxFileSizeBytes"] = "10485760"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(handler);
                services.AddHttpClient("Webhook")
                    .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<RecordingWebhookHandler>());
            });
        });
    }

    private static async Task RegisterWebhookAsync(HttpClient client, string secret)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/flows/{FlowId}/webhooks",
            new { url = $"https://webhook.test/{Guid.NewGuid():N}", secret });
        EnsureStatus(response.StatusCode, HttpStatusCode.Created, "Integration callback setup", "Failed to register webhook.");
    }

    private static async Task ClearWebhooksAsync(HttpClient client)
    {
        var response = await client.GetAsync($"/api/flows/{FlowId}/webhooks");
        EnsureStatus(response.StatusCode, HttpStatusCode.OK, "Integration callback setup", "Failed to list webhooks.");

        var webhooks = (await response.Content.ReadFromJsonAsync<List<WebhookResponse>>(JsonOptions))!;
        foreach (var webhook in webhooks)
        {
            var deleteResponse = await client.DeleteAsync($"/api/flows/{FlowId}/webhooks/{webhook.Id}");
            EnsureStatus(deleteResponse.StatusCode, HttpStatusCode.NoContent, "Integration callback setup", "Failed to clear existing webhook registration.");
        }
    }

    private static async Task<SessionStepResponse> StartSessionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/workflow/sessions/start", new { flowId = FlowId });
        EnsureStatus(response.StatusCode, HttpStatusCode.OK, "UI", "Failed to start session.");
        return (await response.Content.ReadFromJsonAsync<SessionStepResponse>(JsonOptions))!;
    }

    private static async Task<SessionStepResponse> SubmitStepAsync(HttpClient client, Guid sessionId, Guid nodeId, SubmitStepRequest request, string stage)
    {
        var response = await client.PostAsJsonAsync($"/api/workflow/sessions/{sessionId}/steps/{nodeId}/submit", request);
        EnsureStatus(response.StatusCode, HttpStatusCode.OK, stage, "Step submission failed.");
        return (await response.Content.ReadFromJsonAsync<SessionStepResponse>(JsonOptions))!;
    }

    private static async Task<IReadOnlyList<WebhookDeliveryResponse>> ListDeliveriesAsync(HttpClient client)
    {
        var response = await client.GetAsync($"/api/flows/{FlowId}/webhook-deliveries");
        EnsureStatus(response.StatusCode, HttpStatusCode.OK, "Integration callback", "Failed to fetch webhook delivery logs.");
        return (await response.Content.ReadFromJsonAsync<List<WebhookDeliveryResponse>>(JsonOptions))!;
    }

    private static async Task<IReadOnlyList<WebhookDeliveryResponse>> WaitForDeliveryAsync(HttpClient client, Guid sessionId, TimeSpan timeout)
    {
        IReadOnlyList<WebhookDeliveryResponse> deliveries = [];
        await WaitForConditionAsync(async () =>
        {
            deliveries = await ListDeliveriesAsync(client);
            return deliveries.Any(d => d.SessionId == sessionId && d.EventType == "session-completed");
        }, timeout, "[Integration callback] Timed out waiting for webhook delivery log entry.");

        return deliveries;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, string timeoutMessage)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new XunitException(timeoutMessage);
    }

    private static async Task WaitForConditionAsync(Func<Task<bool>> condition, TimeSpan timeout, string timeoutMessage)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new XunitException(timeoutMessage);
    }

    private static void EnsureStatus(HttpStatusCode actual, HttpStatusCode expected, string stage, string action)
    {
        if (actual != expected)
        {
            throw new XunitException($"[{stage}] {action} Expected {(int)expected}, got {(int)actual}.");
        }
    }

    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private sealed record SubmitStepRequest(Dictionary<string, object?> Payload);

    private sealed class SessionStepResponse
    {
        public Guid SessionId { get; set; }
        public bool IsCompleted { get; set; }
        public NodeDto? CurrentNode { get; set; }
    }

    private sealed class NodeDto
    {
        public Guid Id { get; set; }
        public string Key { get; set; } = string.Empty;
    }

    private sealed class WebhookDeliveryResponse
    {
        public Guid SessionId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public string Status { get; set; } = string.Empty;
        public int? LastStatusCode { get; set; }
    }

    private sealed class WebhookResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class RecordingWebhookHandler(IReadOnlyList<HttpStatusCode> statuses) : HttpMessageHandler
    {
        private int _index;
        public List<CallbackAttempt> Attempts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("X-Webhook-Signature", out var signatureValues);
            Attempts.Add(new CallbackAttempt(body, signatureValues?.FirstOrDefault()));

            var responseIndex = Math.Min(_index, statuses.Count - 1);
            var status = statuses[responseIndex];
            _index++;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent($"status={(int)status}")
            };
        }
    }

    private sealed record CallbackAttempt(string Body, string? Signature);
}
