namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// The state of a session immediately after it is started, together with the credential the
/// applicant's browser uses for the rest of the journey.
/// </summary>
public sealed class SessionStartResponse
{
    /// <summary>The unique identifier of the onboarding session.</summary>
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

    /// <summary>
    /// Bearer token authorising requests for this session only. Present when the session was
    /// started anonymously by an applicant; <c>null</c> when an operator started it, as the
    /// operator's own credential already covers the session.
    /// </summary>
    public string? ApplicantToken { get; set; }

    /// <summary>When <see cref="ApplicantToken"/> stops being accepted.</summary>
    public DateTimeOffset? ApplicantTokenExpiresAt { get; set; }

    public static SessionStartResponse From(SessionStepResponse step) => new()
    {
        SessionId = step.SessionId,
        IsCompleted = step.IsCompleted,
        CurrentNode = step.CurrentNode
    };
}
