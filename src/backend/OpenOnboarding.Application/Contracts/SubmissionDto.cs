namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// Represents a step submission made during an onboarding session.
/// </summary>
public sealed class SubmissionDto
{
    /// <summary>The unique identifier of the submission.</summary>
    public Guid Id { get; set; }

    /// <summary>The session this submission belongs to.</summary>
    public Guid SessionId { get; set; }

    /// <summary>The node (step) that was submitted.</summary>
    public Guid NodeId { get; set; }

    /// <summary>The machine-readable key of the node that was submitted.</summary>
    public string NodeKey { get; set; } = string.Empty;

    /// <summary>When the submission was recorded.</summary>
    public DateTimeOffset SubmittedAt { get; set; }

    /// <summary>The raw JSON payload provided by the user for this step.</summary>
    public string DataJson { get; set; } = "{}";
}
