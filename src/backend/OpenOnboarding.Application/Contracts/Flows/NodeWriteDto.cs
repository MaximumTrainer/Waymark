using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Application.Contracts.Flows;

public sealed class NodeWriteDto
{
    /// <summary>
    /// Client-assigned identifier used to correlate this node with connection sourceNodeId/targetNodeId.
    /// Defaults to a new Guid so connections can reference it. Must be set explicitly when connections reference this node.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public NodeType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string JsonContent { get; set; } = "{}";
    public string? ComplianceRuleJson { get; set; }
    public bool IsStartNode { get; set; }
}
