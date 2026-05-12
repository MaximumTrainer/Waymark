namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// Aggregate analytics for a workflow flow.
/// </summary>
public sealed class FlowStatsDto
{
    /// <summary>Total number of sessions ever started for this flow.</summary>
    public int TotalSessions { get; set; }

    /// <summary>Number of sessions that reached the Completed status.</summary>
    public int CompletedSessions { get; set; }

    /// <summary>Number of sessions that were abandoned.</summary>
    public int AbandonedSessions { get; set; }

    /// <summary>
    /// Average time in seconds to complete the flow, calculated only from completed sessions.
    /// Zero when there are no completed sessions.
    /// </summary>
    public double AverageCompletionTimeSeconds { get; set; }

    /// <summary>
    /// Count of abandoned sessions grouped by the node key at which they dropped off.
    /// </summary>
    public Dictionary<string, int> DropOffByNodeKey { get; set; } = new();
}
