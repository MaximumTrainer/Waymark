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

    /// <summary>
    /// A replacement applicant token, extending the credential for a journey still in progress.
    /// <para>
    /// Set on step submission for applicant callers only. Without it a compliance-heavy journey
    /// left open past the token lifetime returns to a live session and a credential the API no
    /// longer accepts, with no way forward but to start again and lose everything entered.
    /// </para>
    /// <c>null</c> when the caller is an operator, whose own credential already covers the session,
    /// and on responses that do not renew - session start carries its token on
    /// <see cref="SessionStartResponse"/> instead.
    /// </summary>
    public string? ApplicantToken { get; set; }

    /// <summary>When <see cref="ApplicantToken"/> stops being accepted.</summary>
    public DateTimeOffset? ApplicantTokenExpiresAt { get; set; }
}
