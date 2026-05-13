namespace OpenOnboarding.Application.Contracts;

public sealed class ComplianceViolation
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? RuleId { get; set; }
}
