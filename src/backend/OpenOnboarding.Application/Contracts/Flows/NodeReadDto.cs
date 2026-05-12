using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Application.Contracts.Flows;

public sealed class NodeReadDto
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public string Key { get; set; } = string.Empty;
    public NodeType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string JsonContent { get; set; } = "{}";
    public string? ComplianceRuleJson { get; set; }
    public bool IsStartNode { get; set; }
}
