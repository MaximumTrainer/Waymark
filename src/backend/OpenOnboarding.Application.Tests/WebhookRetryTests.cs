using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Tests.TestHelpers;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class WebhookRetryTests
{
    // ── Fake ─────────────────────────────────────────────────────────────────

    private sealed class FakeWebhookHttpClient : IWebhookHttpClient
    {
        private readonly Queue<WebhookHttpResponse> _responses;
        public List<(string Url, string Payload)> Calls { get; } = new();

        public FakeWebhookHttpClient(params WebhookHttpResponse[] responses)
            => _responses = new Queue<WebhookHttpResponse>(responses);

        public Task<WebhookHttpResponse> SendAsync(string url, string payloadJson, string signature, CancellationToken ct = default)
        {
            Calls.Add((url, payloadJson));
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : new WebhookHttpResponse(200, "ok", true);
            return Task.FromResult(response);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static OnboardingDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OnboardingDbContext(options);
    }

    private static async Task<(Flow flow, Webhook webhook)> SeedFlowAndWebhookAsync(OnboardingDbContext db)
    {
        var flow = new Flow { Name = "Retry Test Flow" };
        db.Flows.Add(flow);

        var webhook = new Webhook
        {
            FlowId = flow.Id,
            Url = "https://example.com/webhook",
            Secret = "test-secret"
        };
        db.Webhooks.Add(webhook);
        await db.SaveChangesAsync();
        return (flow, webhook);
    }

    private static WebhookService CreateService(
        OnboardingDbContext db,
        IWebhookHttpClient fakeClient,
        Func<int, CancellationToken, Task>? delayProvider = null)
    {
        // No-op delay by default so tests run fast
        delayProvider ??= (_, _) => Task.CompletedTask;
        return new WebhookService(db, fakeClient, new NoOpMetricsService(), delayProvider);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryWebhook_OnFirstFailure_AttemptsMultipleTimes()
    {
        var db = BuildDbContext();
        var (flow, _) = await SeedFlowAndWebhookAsync(db);

        var fakeClient = new FakeWebhookHttpClient(
            new WebhookHttpResponse(500, "error", false),
            new WebhookHttpResponse(200, "ok", true));

        var service = CreateService(db, fakeClient);

        await service.DeliverAsync(Guid.NewGuid(), flow.Id, "session.completed", new { });

        var delivery = await db.WebhookDeliveries.SingleAsync();
        Assert.Equal("delivered", delivery.Status);
        Assert.Equal(2, delivery.AttemptCount);
    }

    [Fact]
    public async Task RetryWebhook_ExceedingMaxAttempts_MarksDeliveryAsFailed()
    {
        var db = BuildDbContext();
        var (flow, _) = await SeedFlowAndWebhookAsync(db);

        var fakeClient = new FakeWebhookHttpClient(
            new WebhookHttpResponse(500, "error", false),
            new WebhookHttpResponse(500, "error", false),
            new WebhookHttpResponse(500, "error", false));

        var service = CreateService(db, fakeClient);

        await service.DeliverAsync(Guid.NewGuid(), flow.Id, "session.completed", new { });

        var delivery = await db.WebhookDeliveries.SingleAsync();
        Assert.Equal("failed", delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
    }

    [Fact]
    public async Task RetryWebhook_OnSuccessAfterRetry_MarksDeliveryAsDelivered()
    {
        var db = BuildDbContext();
        var (flow, _) = await SeedFlowAndWebhookAsync(db);

        var fakeClient = new FakeWebhookHttpClient(
            new WebhookHttpResponse(500, "error", false),
            new WebhookHttpResponse(500, "error", false),
            new WebhookHttpResponse(200, "ok", true));

        var service = CreateService(db, fakeClient);

        await service.DeliverAsync(Guid.NewGuid(), flow.Id, "session.completed", new { });

        var delivery = await db.WebhookDeliveries.SingleAsync();
        Assert.Equal("delivered", delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
    }

    [Fact]
    public async Task RetryWebhook_NoDelay_WhenUsingFakeClient_AllAttemptsComplete()
    {
        var db = BuildDbContext();
        var (flow, _) = await SeedFlowAndWebhookAsync(db);

        var delayCallCount = 0;
        Func<int, CancellationToken, Task> noOpDelay = (_, _) =>
        {
            delayCallCount++;
            return Task.CompletedTask;
        };

        // All three attempts fail so we verify all 3 attempts are made
        var fakeClient = new FakeWebhookHttpClient(
            new WebhookHttpResponse(500, "error", false),
            new WebhookHttpResponse(500, "error", false),
            new WebhookHttpResponse(500, "error", false));

        var service = CreateService(db, fakeClient, noOpDelay);

        var before = DateTimeOffset.UtcNow;
        await service.DeliverAsync(Guid.NewGuid(), flow.Id, "session.completed", new { });
        var elapsed = DateTimeOffset.UtcNow - before;

        Assert.Equal(3, fakeClient.Calls.Count);
        Assert.Equal(2, delayCallCount); // delays only occur between attempts
        Assert.True(elapsed.TotalMilliseconds < 1000, $"Expected fast completion but took {elapsed.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task DeliverAsync_CancelledBeforeFirstAttempt_SavesCancelledStatus()
    {
        var db = BuildDbContext();
        var (flow, _) = await SeedFlowAndWebhookAsync(db);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled

        // Client that throws OperationCanceledException to simulate pre-cancelled send
        var fakeClient = new CancellingWebhookHttpClient();
        Func<int, CancellationToken, Task> delay = (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        var service = new WebhookService(db, fakeClient, new NoOpMetricsService(), delay);

        await service.DeliverAsync(Guid.NewGuid(), flow.Id, "session.completed", new { }, cts.Token);

        var delivery = await db.WebhookDeliveries.SingleAsync();
        Assert.Equal("cancelled", delivery.Status);
    }

    [Fact]
    public async Task DeliverAsync_CancelledDuringRetryDelay_SavesCancelledStatus()
    {
        var db = BuildDbContext();
        var (flow, _) = await SeedFlowAndWebhookAsync(db);

        using var cts = new CancellationTokenSource();

        var firstAttempt = true;
        Func<int, CancellationToken, Task> delay = (_, ct) =>
        {
            if (firstAttempt)
            {
                firstAttempt = false;
                cts.Cancel(); // cancel during the first retry delay
                ct.ThrowIfCancellationRequested();
            }
            return Task.CompletedTask;
        };

        var fakeClient = new FakeWebhookHttpClient(new WebhookHttpResponse(500, "error", false));
        var service = new WebhookService(db, fakeClient, new NoOpMetricsService(), delay);

        await service.DeliverAsync(Guid.NewGuid(), flow.Id, "session.completed", new { }, cts.Token);

        var delivery = await db.WebhookDeliveries.SingleAsync();
        Assert.Equal("cancelled", delivery.Status);
    }

    private sealed class CancellingWebhookHttpClient : IWebhookHttpClient
    {
        public Task<WebhookHttpResponse> SendAsync(string url, string payloadJson, string signature, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new WebhookHttpResponse(200, "ok", true));
        }
    }
}