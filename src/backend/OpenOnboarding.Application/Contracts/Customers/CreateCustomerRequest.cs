namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// Represents a request to create a new customer profile.
/// </summary>
public sealed class CreateCustomerRequest
{
    public string ExternalCustomerId { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}
