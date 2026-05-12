namespace OpenOnboarding.Application.Contracts;

public sealed class SubmitStepRequest
{
    public Dictionary<string, object?> Payload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
