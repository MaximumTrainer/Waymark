namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// An inline customer profile included with a start-session request to avoid a separate round-trip.
/// The profile is upserted by <see cref="ExternalCustomerId"/>: if one already exists it is reused,
/// otherwise a new profile is created.
/// </summary>
public sealed class InlineCustomerProfileRequest
{
    public string ExternalCustomerId { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}
