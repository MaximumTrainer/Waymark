using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class WebhookService(
    OnboardingDbContext dbContext,
    IWebhookHttpClient webhookHttpClient,
    Func<int, CancellationToken, Task>? delayProvider = null) : IWebhookService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<int, CancellationToken, Task> _delay = delayProvider ?? ((ms, ct) => Task.Delay(ms, ct));

    public async Task<WebhookDto> RegisterAsync(Guid flowId, string url, string secret, CancellationToken cancellationToken = default)
    {
        var flowExists = await dbContext.Flows.AnyAsync(f => f.Id == flowId, cancellationToken);
        if (!flowExists)
            throw new NotFoundException($"Flow '{flowId}' not found.");

        var duplicateExists = await dbContext.Webhooks
            .AnyAsync(w => w.FlowId == flowId && w.Url == url, cancellationToken);
        if (duplicateExists)
            throw new ConflictException($"A webhook with URL '{url}' is already registered for flow '{flowId}'.");

        var webhook = new Webhook
        {
            FlowId = flowId,
            Url = url,
            Secret = secret
        };

        dbContext.Webhooks.Add(webhook);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(webhook);
    }

    public async Task<IReadOnlyList<WebhookDto>> ListAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        var webhooks = await dbContext.Webhooks
            .Where(w => w.FlowId == flowId)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        return webhooks.Select(ToDto).ToList();
    }

    public async Task DeleteAsync(Guid flowId, Guid webhookId, CancellationToken cancellationToken = default)
    {
        var webhook = await dbContext.Webhooks
            .FirstOrDefaultAsync(w => w.Id == webhookId && w.FlowId == flowId, cancellationToken)
            ?? throw new NotFoundException($"Webhook '{webhookId}' not found for flow '{flowId}'.");

        dbContext.Webhooks.Remove(webhook);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookDeliveryDto>> ListDeliveriesAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        var deliveries = await dbContext.WebhookDeliveries
            .Include(d => d.Webhook)
            .Where(d => d.Webhook.FlowId == flowId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        return deliveries.Select(ToDeliveryDto).ToList();
    }

    public async Task DeliverAsync(Guid sessionId, Guid flowId, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        var webhooks = await dbContext.Webhooks
            .Where(w => w.FlowId == flowId)
            .ToListAsync(cancellationToken);

        if (webhooks.Count == 0) return;

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        foreach (var webhook in webhooks)
        {
            await DeliverToWebhookAsync(webhook, sessionId, eventType, payloadJson);
        }
    }

    private async Task DeliverToWebhookAsync(Webhook webhook, Guid sessionId, string eventType, string payloadJson)
    {
        var delivery = new WebhookDelivery
        {
            WebhookId = webhook.Id,
            SessionId = sessionId,
            EventType = eventType,
            PayloadJson = payloadJson
        };

        dbContext.WebhookDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync();

        var delays = new[] { 1000, 2000, 4000 };
        var signature = ComputeSignature(payloadJson, webhook.Secret);

        for (var attempt = 0; attempt <= 2; attempt++)
        {
            if (attempt > 0)
                await _delay(delays[attempt - 1], CancellationToken.None);

            var result = await webhookHttpClient.SendAsync(webhook.Url, payloadJson, signature);
            delivery.AttemptCount++;
            delivery.LastStatusCode = result.StatusCode == 0 ? null : result.StatusCode;
            delivery.LastResponseBody = result.Body;

            if (result.IsSuccess)
            {
                delivery.Status = "delivered";
                delivery.DeliveredAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync();
                return;
            }
        }

        delivery.Status = "failed";
        await dbContext.SaveChangesAsync();
    }

    private static string ComputeSignature(string payload, string secret)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(payloadBytes)).ToLowerInvariant();
    }

    private static WebhookDto ToDto(Webhook w) =>
        new(w.Id, w.FlowId, w.Url, w.CreatedAt);

    private static WebhookDeliveryDto ToDeliveryDto(WebhookDelivery d) =>
        new(d.Id, d.WebhookId, d.SessionId, d.EventType, d.AttemptCount, d.Status, d.LastStatusCode, d.CreatedAt, d.DeliveredAt);
}