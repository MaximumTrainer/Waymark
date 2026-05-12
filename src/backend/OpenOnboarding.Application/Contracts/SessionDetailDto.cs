using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Application.Contracts;

/// <summary>
/// Full detail view of an onboarding session.
/// </summary>
public sealed class SessionDetailDto
{
    /// <summary>The unique identifier of the session.</summary>
    public Guid Id { get; set; }

    /// <summary>The flow this session belongs to.</summary>
    public Guid FlowId { get; set; }

    /// <summary>The current lifecycle status of the session.</summary>
    public SessionStatus Status { get; set; }

    /// <summary>When the session was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the session was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The associated customer profile, if any.</summary>
    public Guid? CustomerProfileId { get; set; }

    /// <summary>
    /// The current node the session is on.
    /// <c>null</c> when the session is completed or abandoned.
    /// </summary>
    public NodeDto? CurrentNode { get; set; }
}
