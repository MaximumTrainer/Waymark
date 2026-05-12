namespace OpenOnboarding.Application.Contracts;

public sealed class SessionStepResponse
{
    public Guid SessionId { get; set; }
    public bool IsCompleted { get; set; }
    public NodeDto? CurrentNode { get; set; }
}
