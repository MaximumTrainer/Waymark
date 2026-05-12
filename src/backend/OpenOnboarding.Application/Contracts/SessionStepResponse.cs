namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// Represents the state of a workflow session after starting or submitting a step.
/// </summary>
public sealed class SessionStepResponse
{
    /// <summary>
    /// The unique identifier of the onboarding session.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Indicates whether the onboarding workflow has been completed.
    /// When <c>true</c>, <see cref="CurrentNode"/> will be <c>null</c>.
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// The current workflow node/step to be rendered to the user.
    /// <c>null</c> when the session is completed.
    /// </summary>
    public NodeDto? CurrentNode { get; set; }
}
