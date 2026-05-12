namespace OpenOnboarding.Domain.Entities;

public sealed class Webhook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlowId { get; set; }
    public Flow Flow { get; set; } = null!;
    public string Url { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();
}
