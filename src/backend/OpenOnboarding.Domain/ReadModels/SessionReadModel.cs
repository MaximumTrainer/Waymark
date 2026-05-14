namespace OpenOnboarding.Domain.ReadModels;
/// <summary>Denormalized read-side projection of a workflow session. Updated by projectors on domain events.</summary>
public class SessionReadModel
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public string FlowName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? CustomerCountry { get; set; }
    public string? ExternalCustomerId { get; set; }
    public Guid? CurrentNodeId { get; set; }
    public string? CurrentNodeKey { get; set; }
    public string? CurrentNodeTitle { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int StepCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? AbandonedAt { get; set; }
    public double? CompletionDurationSeconds { get; set; }
}
