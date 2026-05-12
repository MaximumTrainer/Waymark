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

    /// <summary>
    /// An optional inline customer profile. When provided the profile is upserted by
    /// <see cref="InlineCustomerProfileRequest.ExternalCustomerId"/> and the resulting
    /// profile ID is used for the session, making a separate customer-create call unnecessary.
    /// </summary>
    public InlineCustomerProfileRequest? CustomerProfile { get; set; }
}
