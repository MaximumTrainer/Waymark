using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OpenOnboarding.Api.Authorization;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Api.Authentication;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Application.Tests.Authorization;

public sealed class SessionOwnershipHandlerTests
{
    private static readonly SessionOwnershipRequirement Requirement = SessionOwnershipRequirement.Read;
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

    private static SessionDetailDto BuildSession(
        Guid? customerProfileId = null,
        SessionStatus status = SessionStatus.Started) =>
        new()
        {
            Id = Guid.NewGuid(),
            FlowId = Guid.NewGuid(),
            CustomerProfileId = customerProfileId,
            Status = status
        };

    /// <summary>A principal carrying a per-session applicant token for the given session.</summary>
    private static ClaimsPrincipal BuildSessionTokenPrincipal(Guid sessionId)
        => new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, sessionId.ToString()),
                new Claim(ClaimTypes.Role, AppRoles.Applicant),
                new Claim(ApplicantSessionAuthenticationDefaults.SessionIdClaim, sessionId.ToString())
            ],
            "Test"));

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

    // ── Terminal sessions ──────────────────────────────────────────────────────
    //
    // A token issued at session start stays valid until it expires - up to a day. Once the journey
    // it names is over, that credential outlives the visit that created it, which on a shared or
    // public machine is exactly the window worth closing.

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Abandoned)]
    public async Task Write_AgainstATerminalSession_IsRefusedForAnApplicantToken(SessionStatus status)
    {
        var session = BuildSession(status: status);
        var user = BuildSessionTokenPrincipal(session.Id);

        var context = new AuthorizationHandlerContext([SessionOwnershipRequirement.Write], user, session);
        await Handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Abandoned)]
    public async Task Read_AgainstATerminalSession_StillServesItsOwnToken(SessionStatus status)
    {
        // The completion screen renders after the session is Completed; refusing the read would
        // break the last thing the applicant sees.
        var session = BuildSession(status: status);
        var user = BuildSessionTokenPrincipal(session.Id);

        var context = new AuthorizationHandlerContext([SessionOwnershipRequirement.Read], user, session);
        await Handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData(SessionStatus.Started)]
    [InlineData(SessionStatus.Error)]
    public async Task Write_AgainstALiveSession_StillSucceeds(SessionStatus status)
    {
        var session = BuildSession(status: status);
        var user = BuildSessionTokenPrincipal(session.Id);

        var context = new AuthorizationHandlerContext([SessionOwnershipRequirement.Write], user, session);
        await Handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Abandoned)]
    public async Task Write_AgainstATerminalSession_IsStillAllowedForAnOperator(SessionStatus status)
    {
        // The rule is about a stale applicant credential, not about who may act on a finished
        // session. An operator reaches every session regardless of status.
        var session = BuildSession(status: status);
        var user = BuildPrincipal(AppRoles.Operator);

        var context = new AuthorizationHandlerContext([SessionOwnershipRequirement.Write], user, session);
        await Handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }
}
