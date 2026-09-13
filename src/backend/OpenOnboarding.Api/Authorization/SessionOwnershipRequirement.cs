using Microsoft.AspNetCore.Authorization;

namespace OpenOnboarding.Api.Authorization;

/// <summary>
/// Requirement that the current user owns the session (or is an Operator).
/// Used with resource-based authorization against <c>SessionDetailDto</c>.
/// </summary>
public sealed class SessionOwnershipRequirement : IAuthorizationRequirement
{
    private SessionOwnershipRequirement(bool requiresActiveSession)
        => RequiresActiveSession = requiresActiveSession;

    /// <summary>
    /// Whether an applicant token is refused once its session has reached a terminal status.
    /// Operators are never subject to this; the rule is about a credential outliving the visit that
    /// created it, not about who may act on a finished session.
    /// </summary>
    public bool RequiresActiveSession { get; }

    /// <summary>
    /// For reads. A completed session is still readable by the token that completed it, which is
    /// what lets the completion screen render before the applicant closes the tab.
    /// </summary>
    public static SessionOwnershipRequirement Read { get; } = new(requiresActiveSession: false);

    /// <summary>
    /// For anything that changes the session or writes against it. Once a session is Completed or
    /// Abandoned its token stops being accepted here, so a credential left behind on a shared
    /// machine cannot be used to write against the journey that ended.
    /// </summary>
    public static SessionOwnershipRequirement Write { get; } = new(requiresActiveSession: true);
}
