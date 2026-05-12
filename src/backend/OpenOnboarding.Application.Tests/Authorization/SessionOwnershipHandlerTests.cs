using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OpenOnboarding.Api.Authorization;
using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Tests.Authorization;

public sealed class SessionOwnershipHandlerTests
{
    private static readonly SessionOwnershipRequirement Requirement = new();
    private static readonly SessionOwnershipHandler Handler = new();

    private static ClaimsPrincipal BuildPrincipal(string role, string? customerProfileId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-1"),
            new(ClaimTypes.Role, role)
        };
        if (customerProfileId is not null)
            claims.Add(new Claim("customerProfileId", customerProfileId));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static SessionDetailDto BuildSession(Guid? customerProfileId = null) =>
        new() { Id = Guid.NewGuid(), FlowId = Guid.NewGuid(), CustomerProfileId = customerProfileId };

    [Fact]
    public async Task HandleRequirementAsync_WhenOperator_Succeeds()
    {
        var profileId = Guid.NewGuid();
        var user = BuildPrincipal(AppRoles.Operator, profileId.ToString());
        var session = BuildSession(profileId);

        var context = new AuthorizationHandlerContext([Requirement], user, session);
        await Handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenApplicantOwnsSession_Succeeds()
    {
        var profileId = Guid.NewGuid();
        var user = BuildPrincipal(AppRoles.Applicant, profileId.ToString());
        var session = BuildSession(profileId);

        var context = new AuthorizationHandlerContext([Requirement], user, session);
        await Handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenApplicantDoesNotOwnSession_DoesNotSucceed()
    {
        var user = BuildPrincipal(AppRoles.Applicant, Guid.NewGuid().ToString());
        var session = BuildSession(Guid.NewGuid()); // different owner

        var context = new AuthorizationHandlerContext([Requirement], user, session);
        await Handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenApplicantHasNoCustomerProfileIdClaim_DoesNotSucceed()
    {
        var user = BuildPrincipal(AppRoles.Applicant); // no customerProfileId claim
        var session = BuildSession(Guid.NewGuid());

        var context = new AuthorizationHandlerContext([Requirement], user, session);
        await Handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
