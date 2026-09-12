using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OpenOnboarding.Api.Authentication;
using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Api.Authorization;

/// <summary>
/// Resource-based authorization handler that succeeds when the current user is an Operator, holds
/// an applicant session token naming this session, or is an Applicant whose
/// <c>customerProfileId</c> claim matches the session's CustomerProfileId.
/// </summary>
public sealed class SessionOwnershipHandler
    : AuthorizationHandler<SessionOwnershipRequirement, SessionDetailDto>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SessionOwnershipRequirement requirement,
        SessionDetailDto resource)
    {
        if (context.User.IsInRole(AppRoles.Operator))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // A per-session applicant token names the one session it may act on. This is the path the
        // public onboarding app takes; it does not depend on a customer profile existing.
        var sessionId = context.User.FindFirstValue(ApplicantSessionAuthenticationDefaults.SessionIdClaim);
        if (sessionId is not null && resource.Id.ToString() == sessionId)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var customerProfileId = context.User.FindFirstValue(ApplicantSessionAuthenticationDefaults.CustomerProfileIdClaim);
        if (customerProfileId is not null
            && resource.CustomerProfileId?.ToString() == customerProfileId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
