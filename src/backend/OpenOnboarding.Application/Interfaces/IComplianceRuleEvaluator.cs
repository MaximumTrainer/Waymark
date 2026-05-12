using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Domain.Entities;

namespace OpenOnboarding.Application.Interfaces;

public interface IComplianceRuleEvaluator
{
    IReadOnlyList<ComplianceViolation> Evaluate(
        Node node,
        IReadOnlyDictionary<string, object?> payload,
        IReadOnlyList<Submission> previousSubmissions);
}
