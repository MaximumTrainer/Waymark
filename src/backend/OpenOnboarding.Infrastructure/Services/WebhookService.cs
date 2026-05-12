using System.Net.Http.Headers;
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

public sealed class WebhookService(OnboardingDbContext dbContext, IHttpClientFactory httpClientFactory) : IWebhookService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WebhookDto> RegisterAsync(Guid flowId, string url, string secret, CancellationToken cancellationToken = default)
    {
        var flowExists = await dbContext.Flows.AnyAsync(f => f.Id == flowId, cancellationToken);
        if (!flowExists)
            throw new NotFoundException($"Flow '{flowId}' not found.");

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
        var client = httpClientFactory.CreateClient("Webhook");

        for (var attempt = 0; attempt <= 2; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(delays[attempt - 1]);

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                };

                var signature = ComputeSignature(payloadJson, webhook.Secret);
                request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");

                var response = await client.SendAsync(request);
                delivery.AttemptCount++;
                delivery.LastStatusCode = (int)response.StatusCode;
                delivery.LastResponseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    delivery.Status = "delivered";
                    delivery.DeliveredAt = DateTimeOffset.UtcNow;
                    await dbContext.SaveChangesAsync();
                    return;
                }
            }
            catch (Exception ex)
            {
                delivery.AttemptCount++;
                delivery.LastResponseBody = ex.Message;
                delivery.LastStatusCode = null;
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
