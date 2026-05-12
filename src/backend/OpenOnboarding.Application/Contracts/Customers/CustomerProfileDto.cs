namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// Represents a customer profile returned by the API.
/// </summary>
public sealed class CustomerProfileDto
{
    public Guid Id { get; set; }
    public string ExternalCustomerId { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}
