namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// Represents a request to submit the user's answer for the current workflow step.
/// </summary>
public sealed class SubmitStepRequest
{
    /// <summary>
    /// A key-value map containing the step's form field answers.
    /// Keys are field names (case-insensitive); values are the submitted data.
    /// </summary>
    public Dictionary<string, object?> Payload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
