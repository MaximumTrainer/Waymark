using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Tests.PersonaHarness;

/// <summary>
/// Declarative definition of a persona to be exercised through a workflow.
/// </summary>
public sealed class PersonaDefinition
{
    /// <summary>Display name used in the report.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional human-readable description of what this persona represents.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// An optional inline customer profile to attach to the session.
    /// When provided the profile is upserted on session start.
    /// </summary>
    public InlineCustomerProfileRequest? CustomerProfile { get; set; }

    /// <summary>
    /// Ordered list of step submissions for this persona.
    /// Each entry provides the payload to submit at the current node.
    /// </summary>
    public List<PersonaStep> Steps { get; set; } = [];

    /// <summary>
    /// The sequence of node keys the runner expects to visit (in order),
    /// starting from the first node returned by StartSession.
    /// </summary>
    public List<string> ExpectedNodePath { get; set; } = [];

    /// <summary>Whether the session is expected to reach completion.</summary>
    public bool ExpectedCompletion { get; set; }
}

/// <summary>
/// A single step submission within a persona run.
/// </summary>
public sealed class PersonaStep
{
    /// <summary>Form payload to submit at the current node.</summary>
    public Dictionary<string, object?> Payload { get; set; } = [];
}
