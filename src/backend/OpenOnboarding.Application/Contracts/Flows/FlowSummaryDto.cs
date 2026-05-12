namespace OpenOnboarding.Application.Contracts.Flows;

public sealed class FlowSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; }
    public int NodeCount { get; set; }
}
