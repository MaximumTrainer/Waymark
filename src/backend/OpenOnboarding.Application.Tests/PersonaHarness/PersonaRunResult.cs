namespace OpenOnboarding.Application.Tests.PersonaHarness;

/// <summary>
/// The outcome of executing a single persona through the workflow.
/// </summary>
public sealed class PersonaRunResult
{
    /// <summary>Name of the persona that was executed.</summary>
    public string PersonaName { get; set; } = string.Empty;

    /// <summary><c>true</c> when the actual path and completion status matched the expected values.</summary>
    public bool Passed { get; set; }

    /// <summary>Ordered sequence of node keys actually visited during execution.</summary>
    public List<string> ActualNodePath { get; set; } = [];

    /// <summary>Ordered sequence of node keys that were expected to be visited.</summary>
    public List<string> ExpectedNodePath { get; set; } = [];

    /// <summary>Whether the session actually completed.</summary>
    public bool ActualCompletion { get; set; }

    /// <summary>Whether completion was expected.</summary>
    public bool ExpectedCompletion { get; set; }

    /// <summary>
    /// Human-readable explanation of the failure when <see cref="Passed"/> is <c>false</c>.
    /// <c>null</c> on success.
    /// </summary>
    public string? FailureReason { get; set; }
}
