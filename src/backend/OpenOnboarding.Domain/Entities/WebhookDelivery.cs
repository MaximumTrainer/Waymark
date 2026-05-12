namespace OpenOnboarding.Domain.Entities;

public sealed class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WebhookId { get; set; }
    public Webhook Webhook { get; set; } = null!;
    public Guid SessionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public int AttemptCount { get; set; }
    public string Status { get; set; } = "pending";
    public string? LastResponseBody { get; set; }
    public int? LastStatusCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAt { get; set; }
}
