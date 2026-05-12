namespace OpenOnboarding.Application.Contracts;

public sealed record WebhookDto(
    Guid Id,
    Guid FlowId,
    string Url,
    DateTimeOffset CreatedAt);

public sealed record WebhookDeliveryDto(
    Guid Id,
    Guid WebhookId,
    Guid SessionId,
    string EventType,
    int AttemptCount,
    string Status,
    int? LastStatusCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt);
