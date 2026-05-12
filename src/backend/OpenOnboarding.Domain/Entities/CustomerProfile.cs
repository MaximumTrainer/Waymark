namespace OpenOnboarding.Domain.Entities;

public sealed class CustomerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ExternalCustomerId { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";

    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}
