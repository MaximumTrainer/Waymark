using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Api.Authorization;

/// <summary>
/// Resource-based authorization handler that succeeds when the current user is an Operator
/// or is an Applicant whose <c>customerProfileId</c> claim matches the session's CustomerProfileId.
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

        var customerProfileId = context.User.FindFirstValue("customerProfileId");
        if (customerProfileId is not null
            && resource.CustomerProfileId?.ToString() == customerProfileId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
