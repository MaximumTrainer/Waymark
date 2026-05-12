using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Domain.Entities;

public sealed class Connection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlowId { get; set; }
    public Flow Flow { get; set; } = null!;

    public Guid SourceNodeId { get; set; }
    public Guid TargetNodeId { get; set; }

    public string? ConditionField { get; set; }
    public ConditionOperator? ConditionOperator { get; set; }
    public string? ConditionValue { get; set; }
}
