using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Application.Contracts.Flows;

public sealed class ConnectionReadDto
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public Guid SourceNodeId { get; set; }
    public Guid TargetNodeId { get; set; }
    public string? ConditionField { get; set; }
    public ConditionOperator? ConditionOperator { get; set; }
    public string? ConditionValue { get; set; }
    public int Priority { get; set; }
}
