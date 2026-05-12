using Microsoft.AspNetCore.Authorization;

namespace OpenOnboarding.Api.Authorization;

/// <summary>
/// Requirement that the current user owns the session (or is an Operator).
/// Used with resource-based authorization against <c>SessionDetailDto</c>.
/// </summary>
public sealed class SessionOwnershipRequirement : IAuthorizationRequirement { }
