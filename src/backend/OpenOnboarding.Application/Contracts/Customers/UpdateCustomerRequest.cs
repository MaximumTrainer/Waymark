namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// Represents a request to update an existing customer profile.
/// </summary>
public sealed class UpdateCustomerRequest
{
    public string Country { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}
