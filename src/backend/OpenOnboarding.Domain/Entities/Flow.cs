namespace OpenOnboarding.Domain.Entities;

public sealed class Flow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Node> Nodes { get; set; } = new List<Node>();
    public ICollection<Connection> Connections { get; set; } = new List<Connection>();
    public ICollection<FlowVersion> Versions { get; set; } = new List<FlowVersion>();
}
