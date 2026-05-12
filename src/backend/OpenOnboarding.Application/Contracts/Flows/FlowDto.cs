namespace OpenOnboarding.Application.Contracts.Flows;

public sealed class FlowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; }
    public IReadOnlyList<NodeReadDto> Nodes { get; set; } = [];
    public IReadOnlyList<ConnectionReadDto> Connections { get; set; } = [];
}
