using System.Text.Json;

namespace OpenOnboarding.Application.Tests.PersonaHarness;

/// <summary>
/// Aggregated pass/fail report produced after running all personas.
/// </summary>
public sealed class PersonaReport
{
    /// <summary>Individual result for every persona that was executed.</summary>
    public List<PersonaRunResult> Results { get; set; } = [];

    /// <summary>Total number of personas executed.</summary>
    public int TotalCount => Results.Count;

    /// <summary>Number of personas whose execution matched expectations.</summary>
    public int PassCount => Results.Count(r => r.Passed);

    /// <summary>Number of personas whose execution diverged from expectations.</summary>
    public int FailCount => Results.Count(r => !r.Passed);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Returns the report serialised as indented JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
