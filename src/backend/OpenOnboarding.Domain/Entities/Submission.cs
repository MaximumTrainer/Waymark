namespace OpenOnboarding.Domain.Entities;

public sealed class Submission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;

    public Guid NodeId { get; set; }
    public string DataJson { get; set; } = "{}";
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
}
