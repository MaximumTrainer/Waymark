namespace OpenOnboarding.Application.Contracts;

public sealed class StartSessionRequest
{
    public Guid FlowId { get; set; }
    public Guid? CustomerProfileId { get; set; }
}
