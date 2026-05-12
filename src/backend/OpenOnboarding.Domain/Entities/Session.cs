using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Domain.Entities;

public sealed class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlowId { get; set; }
    public Flow Flow { get; set; } = null!;

    public Guid? CurrentNodeId { get; set; }
    public Guid? CustomerProfileId { get; set; }
    public CustomerProfile? CustomerProfile { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Started;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
}
