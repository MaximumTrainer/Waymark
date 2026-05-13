using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Exceptions;

public sealed class ComplianceViolationException(IReadOnlyList<ComplianceViolation> violations)
    : Exception("Compliance violations detected.")
{
    public IReadOnlyList<ComplianceViolation> Violations { get; } = violations;
}
