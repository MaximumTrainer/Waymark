namespace OpenOnboarding.Application.Contracts;

/// <summary>A batch of client-raised analytics events.</summary>
public sealed class AnalyticsIngestRequest
{
    public List<AnalyticsIngestEvent> Events { get; set; } = [];
}

/// <summary>
/// One client-raised event. Deliberately narrower than <see cref="AnalyticsEvent"/>: the server
/// stamps the source and will not accept a caller-supplied one.
/// </summary>
public sealed class AnalyticsIngestEvent
{
    /// <summary>Client-generated id, used to make a retried batch idempotent.</summary>
    public Guid? EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string JourneyId { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string? StepId { get; set; }

    public int? StepIndex { get; set; }

    public Dictionary<string, object?> Payload { get; set; } = [];

    public DateTimeOffset? OccurredAt { get; set; }
}

/// <summary>Outcome of an ingest call.</summary>
public sealed class AnalyticsIngestResponse
{
    public int Accepted { get; set; }
}

/// <summary>
/// Flow-level dashboard figures, shaped for the operator analytics view.
/// </summary>
public sealed class FlowAnalyticsDto
{
    public Guid FlowId { get; set; }
    public string FlowName { get; set; } = string.Empty;
    public int TotalSessions { get; set; }
    public int CompletedSessions { get; set; }
    public int AbandonedSessions { get; set; }
    public double AverageDurationSeconds { get; set; }
    public double CompletionRate { get; set; }
    public string? TopAbandonmentNodeTitle { get; set; }
}
