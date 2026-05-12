using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Domain.Entities;

public sealed class Node
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlowId { get; set; }
    public Flow Flow { get; set; } = null!;

    public string Key { get; set; } = string.Empty;
    public NodeType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string JsonContent { get; set; } = "{}";
    public string? ComplianceRuleJson { get; set; }
    public bool IsStartNode { get; set; }
    public string? ExecutionErrorJson { get; set; }
}
