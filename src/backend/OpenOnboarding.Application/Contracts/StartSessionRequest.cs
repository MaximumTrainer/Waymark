namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// Represents a request to start a new onboarding workflow session.
/// </summary>
public sealed class StartSessionRequest
{
    /// <summary>
    /// The unique identifier of the workflow flow to execute.
    /// </summary>
    public Guid FlowId { get; set; }

    /// <summary>
    /// The optional identifier of the customer profile to associate with this session.
    /// </summary>
    public Guid? CustomerProfileId { get; set; }
}
