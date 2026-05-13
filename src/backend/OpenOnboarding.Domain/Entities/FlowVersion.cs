namespace OpenOnboarding.Domain.Entities;

public class FlowVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlowId { get; set; }
    public int VersionNumber { get; set; }
    public string SnapshotJson { get; set; } = "{}"; // Full FlowDto serialized as JSON
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public virtual Flow Flow { get; set; } = null!;
}
