namespace OpenOnboarding.Domain.Entities;

/// <summary>
/// A persisted journey analytics event.
/// <para>
/// Events were previously written to the application log and nothing else, so the per-step trail
/// that shows where applicants stall could not be queried or exported. Rows here are the durable
/// record; aggregate flow statistics are still derived from sessions and submissions.
/// </para>
/// </summary>
public sealed class AnalyticsEventRecord
{
    /// <summary>The originating event's id. Doubles as the idempotency key for client retries.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public string EventType { get; set; } = string.Empty;

    public string JourneyId { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string? StepId { get; set; }

    public int? StepIndex { get; set; }

    /// <summary>Event-specific payload, stored as JSON so new event types need no schema change.</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>Where the event was raised: <c>server</c> or <c>client</c>.</summary>
    public string Source { get; set; } = AnalyticsEventSources.Server;

    /// <summary>When the event happened, as reported by its origin.</summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the row was written. Retention is measured from this, not <see cref="OccurredAt"/>,
    /// which a client controls and could backdate.
    /// </summary>
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class AnalyticsEventSources
{
    public const string Server = "server";
    public const string Client = "client";
}
