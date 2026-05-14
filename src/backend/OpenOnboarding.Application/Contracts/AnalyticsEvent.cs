namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// Standardised flat-schema analytics event emitted by the journey engine.
/// Designed to be queryable directly in tools like BigQuery or Mixpanel.
/// </summary>
public sealed record AnalyticsEvent
{
    /// <summary>Unique identifier for this event instance.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>The type of event, e.g. session_started, step_viewed, journey_complete.</summary>
    public required string EventType { get; init; }

    /// <summary>Unique identifier for the journey (flow) definition.</summary>
    public required string JourneyId { get; init; }

    /// <summary>Unique identifier for the session instance.</summary>
    public required string SessionId { get; init; }

    /// <summary>Identifier of the current step/node, or null when the session has no current node.</summary>
    public string? StepId { get; init; }

    /// <summary>Zero-based index of the current step within the session's submission history.</summary>
    public int? StepIndex { get; init; }

    /// <summary>Dynamic payload specific to this event type (e.g. which rule failed, which button was clicked).</summary>
    public IReadOnlyDictionary<string, object?> Payload { get; init; } = new Dictionary<string, object?>();

    /// <summary>UTC timestamp when the event occurred.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
