namespace OpenOnboarding.Application.Contracts.Flows;

public sealed class CreateFlowRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<NodeWriteDto> Nodes { get; set; } = [];
    public IReadOnlyList<ConnectionWriteDto> Connections { get; set; } = [];
}
